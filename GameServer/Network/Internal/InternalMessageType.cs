namespace GameServer.Network.Internal;

public enum InternalMessageType : ushort
{
    ServerHello = 0x0001,
    GatewayHello = 0x0002,
    ServerHeartbeat = 0x0003,

    PlayerSessionOpen = 0x0101,
    PlayerSessionOpenAck = 0x0102,
    PlayerSessionClose = 0x0103,
    PlayerKicked = 0x0104,

    PlayerPlayPacket = 0x0201,
    PlayerMoveInput = 0x0202,
    PlayerActionInput = 0x0203,

    ServerPlayPacket = 0x0301,
    ServerPlayPacketBatch = 0x0302,

    TransferPlayer = 0x0401,
    DisconnectPlayer = 0x0402
}
