using GameServer.Application;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Network;

/// <summary>Schedules KeepAlive packets and requests closure for unresponsive sessions.</summary>
public sealed class SessionLivenessService
{
    private readonly GameServerOptions _options;
    private long _nextKeepAliveId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public SessionLivenessService(GameServerOptions options)
    {
        _options = options;
    }

    public void Update(SessionContext session)
    {
        if (session.Closed || session.CloseRequested || session.State != GameSessionState.Play)
            return;

        var now = session.TimeProvider.GetUtcNow();
        var state = session.Liveness;
        state.EnsureScheduled(now, _options.KeepAliveInitialDelay);

        if (state.IsTimedOut(now, _options.KeepAliveTimeout))
        {
            var pendingId = state.PendingKeepAliveId;
            Warn("KeepAlive", session.SessionId, session.PlayerName,
                $"KeepAlive timeout, pendingId={pendingId}, timeout={_options.KeepAliveTimeout.TotalSeconds:F0}s");
            session.RequestClose($"KeepAlive timeout ({pendingId})");
            return;
        }

        if (!state.ShouldSend(now))
            return;

        var keepAliveId = Interlocked.Increment(ref _nextKeepAliveId);
        state.MarkSent(keepAliveId, now, _options.KeepAliveInterval);
        session.EnqueueOutput(S2CPacketBuilders.BuildKeepAlive(keepAliveId));
        Info("KeepAlive", session.SessionId, session.PlayerName, $"S2C KeepAlive enqueued, id={keepAliveId}");
    }
}
