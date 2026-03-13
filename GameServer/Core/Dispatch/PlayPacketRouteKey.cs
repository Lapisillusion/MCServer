namespace GameServer.Core.Dispatch;

public readonly record struct PlayPacketRouteKey(
    GameSessionState State,
    int PacketId);
