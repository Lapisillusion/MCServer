using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Dispatch;
using GameServer.Players;
using GameServer.Tick;

namespace GameServer.Core.Session;

public sealed class SessionContext
{
    private int _closed;
    private int _closeRequested;
    private string? _closeReason;

    // ?? Tick Pipeline queues ???????????????????????????????
    private readonly ConcurrentQueue<InputFrame> _incoming = new();
    private readonly ConcurrentQueue<byte[]> _outgoing = new();

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

    public int IncomingCount => _incoming.Count;
    public int OutgoingCount => _outgoing.Count;

    // ?? Input (network thread ? tick thread) ??????????????

    public void EnqueueInput(int packetId, byte[] frame)
        => _incoming.Enqueue(new InputFrame(packetId, frame, SessionId));

    public bool TryDequeueInput(out InputFrame frame)
        => _incoming.TryDequeue(out frame);

    // ?? Output (handlers ? tick flusher) ??????????????????

    public void EnqueueOutput(byte[] frame)
    {
        if (!Closed && !CloseRequested)
            _outgoing.Enqueue(frame);
    }

    public List<byte[]> DrainAllOutput()
    {
        var result = new List<byte[]>(Math.Min(_outgoing.Count, 64));
        while (_outgoing.TryDequeue(out var frame))
            result.Add(frame);
        return result;
    }

    // ?? Direct send (transitional: only PlayerJoinFlow) ???

    public async Task SendFrameAsync(byte[] frame, CancellationToken ct = default)
    {
        if (Stream == null)
            throw new InvalidOperationException("Session stream is not set.");

        await Stream.WriteAsync(frame, ct);
        await Stream.FlushAsync(ct);
    }

    // ?? Lifecycle ?????????????????????????????????????????

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
