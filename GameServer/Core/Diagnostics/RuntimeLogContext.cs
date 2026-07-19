namespace GameServer.Core.Diagnostics;

public readonly record struct RuntimeLogContext(
    string SessionId,
    string PlayerName,
    string PacketId,
    long TickId,
    // ── Layer 4: Packet identity ──────────────────────────
    string PacketName,
    string Direction,
    // ── Layer 1: Module ───────────────────────────────────
    string Module,
    // ── Layer 3: Entity ───────────────────────────────────
    int EntityId,
    int Dimension,
    byte Gamemode,
    // ── Layer 5: Temporal ─────────────────────────────────
    string Stage)
{
    public static RuntimeLogContext Empty => new(
        SessionId: string.Empty,
        PlayerName: string.Empty,
        PacketId: string.Empty,
        TickId: -1,
        PacketName: string.Empty,
        Direction: string.Empty,
        Module: string.Empty,
        EntityId: -1,
        Dimension: int.MinValue,
        Gamemode: byte.MaxValue,
        Stage: string.Empty);

    // ── Existing With* methods ────────────────────────────

    public RuntimeLogContext WithSessionId(string sessionId) => this with { SessionId = sessionId };

    public RuntimeLogContext WithPlayerName(string playerName) => this with { PlayerName = playerName };

    public RuntimeLogContext WithPacketId(string packetId) => this with { PacketId = packetId };

    public RuntimeLogContext WithTickId(long tickId) => this with { TickId = tickId };

    // ── New With* methods ─────────────────────────────────

    public RuntimeLogContext WithPacketName(string packetName) => this with { PacketName = packetName };

    public RuntimeLogContext WithDirection(string direction) => this with { Direction = direction };

    public RuntimeLogContext WithModule(string module) => this with { Module = module };

    public RuntimeLogContext WithEntityId(int entityId) => this with { EntityId = entityId };

    public RuntimeLogContext WithDimension(int dimension) => this with { Dimension = dimension };

    public RuntimeLogContext WithGamemode(byte gamemode) => this with { Gamemode = gamemode };

    public RuntimeLogContext WithStage(string stage) => this with { Stage = stage };
}
