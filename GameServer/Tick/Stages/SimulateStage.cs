using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Movement;
using GameServer.Network;
using GameServer.World;

namespace GameServer.Tick.Stages;

/// <summary>
/// Runs cross-session simulation services after input has been applied: connection liveness
/// and movement-driven chunk-view reconciliation. Future physics/entity systems join here.
/// </summary>
public sealed class SimulateStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly SessionLivenessService _liveness;
    private readonly ChunkStreamService _chunkStream;
    private readonly PlayerPhysicsService _physics;

    public TickPipelineStage Stage => TickPipelineStage.Simulate;

    public SimulateStage(
        SessionRegistry sessions,
        SessionLivenessService liveness,
        ChunkStreamService chunkStream,
        PlayerPhysicsService physics)
    {
        _sessions = sessions;
        _liveness = liveness;
        _chunkStream = chunkStream;
        _physics = physics;
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        foreach (var (_, session) in _sessions.All)
        {
            _liveness.Update(session);
            _physics.Update(session, tickNumber);

            if (!session.Closed && !session.CloseRequested &&
                session.State == GameSessionState.Play && session.Player?.ChunksSent == true)
            {
                _chunkStream.UpdateView(session);
            }
        }

        return ValueTask.CompletedTask;
    }
}
