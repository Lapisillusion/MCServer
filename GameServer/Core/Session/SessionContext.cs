using System.Net.Sockets;
using GameServer.Core.Dispatch;

namespace GameServer.Core.Session;

public sealed class SessionContext
{
    private int _closed;

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
    public DateTime CreatedUtc { get; }
    public DateTime LastActivityUtc { get; private set; }
    public bool Closed => Volatile.Read(ref _closed) != 0;

    public void Touch() => LastActivityUtc = DateTime.UtcNow;

    public bool TryMarkClosed() => Interlocked.CompareExchange(ref _closed, 1, 0) == 0;
}
