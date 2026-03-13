using System.Collections.Concurrent;
using System.Net.Sockets;

namespace GameServer.Core.Session;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<long, SessionContext> _sessions = new();
    private long _nextSessionId;

    public int Count => _sessions.Count;

    public SessionContext Create(Socket socket)
    {
        var sessionId = Interlocked.Increment(ref _nextSessionId);
        var session = new SessionContext(sessionId, socket);
        if (!_sessions.TryAdd(sessionId, session))
            throw new InvalidOperationException($"Session id collision: {sessionId}");

        return session;
    }

    public bool Remove(long sessionId, out SessionContext? session)
    {
        return _sessions.TryRemove(sessionId, out session);
    }
}
