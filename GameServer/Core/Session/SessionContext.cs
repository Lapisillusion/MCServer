using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Channels;
using GameServer.Core.Dispatch;
using GameServer.Players;
using GameServer.Tick;

namespace GameServer.Core.Session;

public sealed class SessionContext
{
    private int _closed;
    private int _closeRequested;
    private string? _closeReason;

    private readonly ConcurrentQueue<InputFrame> _incoming = new();
    private readonly ConcurrentQueue<byte[]> _outgoing = new();
    private readonly Channel<OutboundBatch> _sendQueue = Channel.CreateUnbounded<OutboundBatch>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

    // Owned by NetworkFlushStage (the sole outgoing-queue consumer).
    private byte[]? _outputCursor;
    private int _outputCursorOffset;
    private int _incomingCount;
    private int _outgoingFrameCount;
    private long _outgoingBytes;
    private long _pendingSendBytes;

    public SessionContext(long sessionId, Socket socket, TimeProvider? timeProvider = null)
    {
        SessionId = sessionId;
        Socket = socket;
        TimeProvider = timeProvider ?? TimeProvider.System;
        State = GameSessionState.New;
        CreatedUtc = TimeProvider.GetUtcNow().UtcDateTime;
        LastActivityUtc = CreatedUtc;
    }

    public long SessionId { get; }
    public Socket Socket { get; }
    public TimeProvider TimeProvider { get; }
    public GameSessionState State { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; }
    public DateTime LastActivityUtc { get; private set; }
    public bool Closed => Volatile.Read(ref _closed) != 0;
    public bool CloseRequested => Volatile.Read(ref _closeRequested) != 0;
    public string? CloseReason => Volatile.Read(ref _closeReason);

    public NetworkStream? Stream { get; set; }
    public PlayerContext? Player { get; set; }
    public SessionLivenessState Liveness { get; } = new();

    public int IncomingCount => Math.Max(0, Volatile.Read(ref _incomingCount));
    public int OutgoingCount => Math.Max(0, Volatile.Read(ref _outgoingFrameCount)) + (_outputCursor == null ? 0 : 1);
    public long OutgoingBytes => Math.Max(0, Volatile.Read(ref _outgoingBytes));
    public long PendingSendBytes => Math.Max(0, Volatile.Read(ref _pendingSendBytes));
    public long TotalPendingOutputBytes => OutgoingBytes + PendingSendBytes;

    public void EnqueueInput(int packetId, byte[] frame)
    {
        Interlocked.Increment(ref _incomingCount);
        _incoming.Enqueue(new InputFrame(packetId, frame, SessionId));
    }

    public bool TryDequeueInput(out InputFrame frame)
    {
        if (!_incoming.TryDequeue(out frame))
            return false;
        Interlocked.Decrement(ref _incomingCount);
        return true;
    }

    public void EnqueueOutput(byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Closed || CloseRequested)
            return;

        Interlocked.Add(ref _outgoingBytes, frame.Length);
        Interlocked.Increment(ref _outgoingFrameCount);
        _outgoing.Enqueue(frame);
    }

    /// <summary>
    /// Copies at most destination.Length bytes while retaining a cursor into oversized frames.
    /// This keeps every network batch bounded without re-enqueuing or allocating frame fragments.
    /// </summary>
    public int CopyOutputTo(Span<byte> destination, out int completedFrames)
    {
        completedFrames = 0;
        var written = 0;

        while (written < destination.Length)
        {
            if (_outputCursor == null)
            {
                if (!_outgoing.TryDequeue(out _outputCursor))
                    break;
                Interlocked.Decrement(ref _outgoingFrameCount);
                _outputCursorOffset = 0;
            }

            var remaining = _outputCursor.Length - _outputCursorOffset;
            var copyLength = Math.Min(remaining, destination.Length - written);
            _outputCursor.AsSpan(_outputCursorOffset, copyLength).CopyTo(destination[written..]);
            _outputCursorOffset += copyLength;
            written += copyLength;
            Interlocked.Add(ref _outgoingBytes, -copyLength);

            if (_outputCursorOffset == _outputCursor.Length)
            {
                _outputCursor = null;
                _outputCursorOffset = 0;
                completedFrames++;
            }
        }

        return written;
    }

    /// <summary>Dequeues one complete raw frame. Used by the optional compression path.</summary>
    public bool TryDequeueOutput(out byte[] frame)
    {
        if (_outputCursor != null)
            throw new InvalidOperationException("Cannot dequeue whole frames while a partial output cursor is active.");

        if (!_outgoing.TryDequeue(out frame!))
            return false;

        Interlocked.Decrement(ref _outgoingFrameCount);
        Interlocked.Add(ref _outgoingBytes, -frame.Length);
        return true;
    }

    /// <summary>Compatibility helper for tests/tools; the hot path uses CopyOutputTo.</summary>
    public List<byte[]> DrainAllOutput()
    {
        var result = new List<byte[]>(Math.Min(OutgoingCount, 64));
        if (_outputCursor != null)
        {
            var remaining = _outputCursor.AsSpan(_outputCursorOffset).ToArray();
            Interlocked.Add(ref _outgoingBytes, -remaining.Length);
            result.Add(remaining);
            _outputCursor = null;
            _outputCursorOffset = 0;
        }

        while (_outgoing.TryDequeue(out var frame))
        {
            Interlocked.Decrement(ref _outgoingFrameCount);
            Interlocked.Add(ref _outgoingBytes, -frame.Length);
            result.Add(frame);
        }
        return result;
    }

    public bool TryQueueSendBatch(OutboundBatch batch, int maxPendingBytes)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Length <= 0)
            throw new ArgumentException("Batch must contain at least one byte.", nameof(batch));
        if (maxPendingBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPendingBytes));
        if (Closed || CloseRequested)
            return false;

        while (true)
        {
            var pending = Volatile.Read(ref _pendingSendBytes);
            if (pending > maxPendingBytes - batch.Length)
                return false;
            if (Interlocked.CompareExchange(ref _pendingSendBytes, pending + batch.Length, pending) == pending)
                break;
        }

        if (_sendQueue.Writer.TryWrite(batch))
            return true;

        Interlocked.Add(ref _pendingSendBytes, -batch.Length);
        return false;
    }

    public ValueTask<bool> WaitToReadSendBatchAsync(CancellationToken ct)
        => _sendQueue.Reader.WaitToReadAsync(ct);

    public bool TryReadSendBatch(out OutboundBatch? batch)
        => _sendQueue.Reader.TryRead(out batch);

    public void ReleaseSendBatch(OutboundBatch batch)
    {
        var length = batch.Length;
        batch.Dispose();
        Interlocked.Add(ref _pendingSendBytes, -length);
    }

    public void CompleteSendQueue() => _sendQueue.Writer.TryComplete();

    public int DrainPendingSendBatches()
    {
        var drained = 0;
        while (_sendQueue.Reader.TryRead(out var batch))
        {
            ReleaseSendBatch(batch);
            drained++;
        }
        return drained;
    }

    // Direct send is intentionally limited to PlayerJoinFlow, before the send pump starts.
    public async Task SendFrameAsync(byte[] frame, CancellationToken ct = default)
    {
        if (Stream == null)
            throw new InvalidOperationException("Session stream is not set.");

        await Stream.WriteAsync(frame, ct);
        await Stream.FlushAsync(ct);
    }

    public void Touch() => LastActivityUtc = TimeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Requests the owning network loop to stop without claiming final cleanup ownership.
    /// The BackendGatewayServer finally block remains the single cleanup path.
    /// </summary>
    public bool RequestClose(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Interlocked.CompareExchange(ref _closeRequested, 1, 0) != 0)
            return false;

        Volatile.Write(ref _closeReason, reason);
        State = GameSessionState.Closing;
        try
        {
            Socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // The socket may already be disconnected; the read loop will still observe CloseRequested.
        }

        return true;
    }

    public bool TryMarkClosed() => Interlocked.CompareExchange(ref _closed, 1, 0) == 0;
}
