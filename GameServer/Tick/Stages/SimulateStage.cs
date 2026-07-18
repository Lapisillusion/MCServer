using GameServer.Core.Session;

namespace GameServer.Tick.Stages;

/// <summary>
/// Runs game logic that spans sessions. Placeholder for v0.3.2;
/// future: entity AI, physics, world updates, scheduled tasks.
/// </summary>
public sealed class SimulateStage : ITickStage
{
    public TickPipelineStage Stage => TickPipelineStage.Simulate;

    public SimulateStage()
    {
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        return ValueTask.CompletedTask;
    }
}
