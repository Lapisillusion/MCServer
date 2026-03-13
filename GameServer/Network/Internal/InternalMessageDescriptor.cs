namespace GameServer.Network.Internal;

public sealed record InternalMessageDescriptor(
    InternalMessageType MessageType,
    InternalMessageDirection Direction,
    string Description);
