using GameServer.Core.Dispatch;
using GameServer.Core.Session;
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

    public TickPipelineStage Stage => TickPipelineStage.Simulate;

    public SimulateStage(
        SessionRegistry sessions,
        SessionLivenessService liveness,
        ChunkStreamService chunkStream)
    {
        _sessions = sessions;
        _liveness = liveness;
        _chunkStream = chunkStream;
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        foreach (var (_, session) in _sessions.All)
        {
            _liveness.Update(session);

            if (!session.Closed && !session.CloseRequested &&
                session.State == GameSessionState.Play && session.Player?.ChunksSent == true)
            {
                _chunkStream.UpdateView(session);
            }
        }

        return ValueTask.CompletedTask;
    }
}
