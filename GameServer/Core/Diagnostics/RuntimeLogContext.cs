namespace GameServer.Core.Diagnostics;

public readonly record struct RuntimeLogContext(
    string SessionId,
    string PlayerId,
    string PacketId,
    long TickId)
{
    public static RuntimeLogContext Empty => new(
        SessionId: string.Empty,
        PlayerId: string.Empty,
        PacketId: string.Empty,
        TickId: -1);

    public RuntimeLogContext WithSessionId(string sessionId) => this with { SessionId = sessionId };

    public RuntimeLogContext WithPlayerId(string playerId) => this with { PlayerId = playerId };

    public RuntimeLogContext WithPacketId(string packetId) => this with { PacketId = packetId };

    public RuntimeLogContext WithTickId(long tickId) => this with { TickId = tickId };
}
