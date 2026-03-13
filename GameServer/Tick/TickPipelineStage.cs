namespace GameServer.Tick;

public enum TickPipelineStage : byte
{
    InputCollect = 0,
    Simulate = 1,
    Replication = 2,
    NetworkFlush = 3
}
