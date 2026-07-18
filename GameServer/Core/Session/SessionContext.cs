using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Dispatch;
using GameServer.Players;
using GameServer.Tick;

namespace GameServer.Core.Session;

public sealed class SessionContext
{
    private int _closed;

    // ── Tick Pipeline queues ───────────────────────────────
    private readonly ConcurrentQueue<InputFrame> _incoming = new();
    private readonly ConcurrentQueue<byte[]> _outgoing = new();

    public SessionContext(long sessionId, Socket socket)
    {
        SessionId = sessionId;
        Socket = socket;
        State = GameSessionState.New;
        CreatedUtc = DateTime.UtcNow;
        LastActivityUtc = CreatedUtc;
    }

    public long SessionId { get; }
    public Socket Socket { get; }
    public GameSessionState State { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; }
    public DateTime LastActivityUtc { get; private set; }
    public bool Closed => Volatile.Read(ref _closed) != 0;

    public NetworkStream? Stream { get; set; }
    public PlayerContext? Player { get; set; }

    public int IncomingCount => _incoming.Count;
    public int OutgoingCount => _outgoing.Count;

    // ── Input (network thread → tick thread) ──────────────

    /// <summary>Called by network read loop to enqueue a received C2S frame.</summary>
    public void EnqueueInput(int packetId, byte[] frame)
        => _incoming.Enqueue(new InputFrame(packetId, frame, SessionId));

    /// <summary>Called by InputCollectStage (tick thread) to drain input.</summary>
    public bool TryDequeueInput(out InputFrame frame)
        => _incoming.TryDequeue(out frame);

    // ── Output (handlers → tick flusher) ──────────────────

    /// <summary>
    /// Called by dispatch handlers to enqueue a S2C frame.
    /// NOT written to socket directly — NetworkFlushStage drains and batches all output.
    /// </summary>
    public void EnqueueOutput(byte[] frame)
        => _outgoing.Enqueue(frame);

    /// <summary>Called by NetworkFlushStage (tick thread) to drain all pending output.</summary>
    public List<byte[]> DrainAllOutput()
    {
        var result = new List<byte[]>(Math.Min(_outgoing.Count, 64));
        while (_outgoing.TryDequeue(out var frame))
            result.Add(frame);
        return result;
    }

    // ── Direct send (transitional: only PlayerJoinFlow) ───

    /// <summary>
    /// Direct async send. ONLY for PlayerJoinFlow (runs before tick pipeline takes over).
    /// All post-join output MUST use EnqueueOutput().
    /// </summary>
    public async Task SendFrameAsync(byte[] frame, CancellationToken ct = default)
    {
        if (Stream == null)
            throw new InvalidOperationException("Session stream is not set.");

        await Stream.WriteAsync(frame, ct);
        await Stream.FlushAsync(ct);
    }

    // ── Lifecycle ─────────────────────────────────────────

    public void Touch() => LastActivityUtc = DateTime.UtcNow;

    public bool TryMarkClosed() => Interlocked.CompareExchange(ref _closed, 1, 0) == 0;
}
