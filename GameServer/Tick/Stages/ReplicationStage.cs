using GameServer.Core.Session;

namespace GameServer.Tick.Stages;

/// <summary>
/// Collects and aggregates per-session output. Placeholder for v0.3.2;
/// future: deduplication, cross-player visibility aggregation.
/// </summary>
public sealed class ReplicationStage : ITickStage
{
    public TickPipelineStage Stage => TickPipelineStage.Replication;

    public ReplicationStage()
    {
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        return ValueTask.CompletedTask;
    }
}
