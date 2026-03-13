namespace GameServer.Network.Internal;

public static class InternalMessageCatalog
{
    public static IReadOnlyDictionary<InternalMessageType, InternalMessageDescriptor> CreateDefault()
    {
        return new Dictionary<InternalMessageType, InternalMessageDescriptor>
        {
            [InternalMessageType.ServerHello] = new(
                InternalMessageType.ServerHello,
                InternalMessageDirection.Bidirectional,
                "建链协商: server hello"),
            [InternalMessageType.GatewayHello] = new(
                InternalMessageType.GatewayHello,
                InternalMessageDirection.Bidirectional,
                "建链协商: gateway hello"),
            [InternalMessageType.ServerHeartbeat] = new(
                InternalMessageType.ServerHeartbeat,
                InternalMessageDirection.GameServerToGateway,
                "GameServer 运行状态上报"),
            [InternalMessageType.PlayerSessionOpen] = new(
                InternalMessageType.PlayerSessionOpen,
                InternalMessageDirection.GatewayToGameServer,
                "玩家会话开通"),
            [InternalMessageType.PlayerSessionOpenAck] = new(
                InternalMessageType.PlayerSessionOpenAck,
                InternalMessageDirection.GameServerToGateway,
                "玩家会话开通确认"),
            [InternalMessageType.PlayerSessionClose] = new(
                InternalMessageType.PlayerSessionClose,
                InternalMessageDirection.Bidirectional,
                "玩家会话关闭"),
            [InternalMessageType.PlayerKicked] = new(
                InternalMessageType.PlayerKicked,
                InternalMessageDirection.GameServerToGateway,
                "踢人请求"),
            [InternalMessageType.PlayerPlayPacket] = new(
                InternalMessageType.PlayerPlayPacket,
                InternalMessageDirection.GatewayToGameServer,
                "Play 原始包透传"),
            [InternalMessageType.PlayerMoveInput] = new(
                InternalMessageType.PlayerMoveInput,
                InternalMessageDirection.GatewayToGameServer,
                "结构化移动输入"),
            [InternalMessageType.PlayerActionInput] = new(
                InternalMessageType.PlayerActionInput,
                InternalMessageDirection.GatewayToGameServer,
                "结构化动作输入"),
            [InternalMessageType.ServerPlayPacket] = new(
                InternalMessageType.ServerPlayPacket,
                InternalMessageDirection.GameServerToGateway,
                "单包下行"),
            [InternalMessageType.ServerPlayPacketBatch] = new(
                InternalMessageType.ServerPlayPacketBatch,
                InternalMessageDirection.GameServerToGateway,
                "批量下行"),
            [InternalMessageType.TransferPlayer] = new(
                InternalMessageType.TransferPlayer,
                InternalMessageDirection.GameServerToGateway,
                "跨服迁移"),
            [InternalMessageType.DisconnectPlayer] = new(
                InternalMessageType.DisconnectPlayer,
                InternalMessageDirection.GameServerToGateway,
                "请求断开玩家")
        };
    }
}
