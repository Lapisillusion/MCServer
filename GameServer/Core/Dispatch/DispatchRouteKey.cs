using GameServer.Network.Internal;

namespace GameServer.Core.Dispatch;

public readonly record struct DispatchRouteKey(
    GameSessionState State,
    InternalMessageType MessageType);
