namespace GameServer.Tick;

/// <summary>Contract for a stage in the 20 TPS pipeline.</summary>
public interface ITickStage
{
    TickPipelineStage Stage { get; }
    ValueTask ExecuteAsync(long tickNumber, CancellationToken ct);
}
