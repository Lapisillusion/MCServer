using GameServer.Core.Diagnostics;
using GameServer.Network.Internal;

namespace GameServer.Core.Dispatch;

public static class M0DispatchBootstrap
{
    public static InternalMessageDispatcher Build()
    {
        var dispatcher = new InternalMessageDispatcher();

        dispatcher.Register(GameSessionState.New, InternalMessageType.PlayerSessionOpen, Noop);
        dispatcher.Register(GameSessionState.Play, InternalMessageType.PlayerPlayPacket, Noop);
        dispatcher.Register(GameSessionState.Play, InternalMessageType.PlayerMoveInput, Noop);
        dispatcher.Register(GameSessionState.Play, InternalMessageType.PlayerActionInput, Noop);
        dispatcher.Register(GameSessionState.New, InternalMessageType.PlayerSessionClose, Noop);
        dispatcher.Register(GameSessionState.Play, InternalMessageType.PlayerSessionClose, Noop);
        dispatcher.Register(GameSessionState.Closing, InternalMessageType.PlayerSessionClose, Noop);

        return dispatcher;
    }

    private static ValueTask Noop(
        in RuntimeLogContext context,
        in DispatchRouteKey route,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
