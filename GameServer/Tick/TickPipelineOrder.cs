namespace GameServer.Tick;

public static class TickPipelineOrder
{
    public static IReadOnlyList<TickPipelineStage> Default { get; } = new[]
    {
        TickPipelineStage.InputCollect,
        TickPipelineStage.Simulate,
        TickPipelineStage.Replication,
        TickPipelineStage.NetworkFlush
    };
}
