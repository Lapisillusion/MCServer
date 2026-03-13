namespace GameServer.Network.Internal;

public enum InternalMessageDirection : byte
{
    Bidirectional = 0,
    GatewayToGameServer = 1,
    GameServerToGateway = 2
}
