using GameServer.Core.Diagnostics;
using GameServer.Core.Session;

namespace GameServer.Core.Dispatch;

public static class M1PlayDispatchBootstrap
{
    // Minecraft Java 1.12.2 (protocol 340), serverbound Play IDs.
    public const int C2S_TeleportConfirm = 0x00;
    public const int C2S_ClientSettings = 0x04;
    public const int C2S_KeepAlive = 0x0B;
    public const int C2S_Player = 0x0C;
    public const int C2S_PlayerPosition = 0x0D;
    public const int C2S_PlayerPositionAndLook = 0x0E;
    public const int C2S_PlayerLook = 0x0F;
    public const int C2S_HeldItemChange = 0x1A;

    public static PlayPacketDispatcher Build()
    {
        var dispatcher = new PlayPacketDispatcher();

        dispatcher.Register(GameSessionState.New, C2S_ClientSettings, Noop);

        dispatcher.Register(GameSessionState.Play, C2S_TeleportConfirm, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_ClientSettings, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_KeepAlive, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_Player, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerPosition, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerPositionAndLook, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerLook, Noop);
        dispatcher.Register(GameSessionState.Play, C2S_HeldItemChange, Noop);

        return dispatcher;
    }

    private static ValueTask Noop(
        SessionContext session,
        in RuntimeLogContext context,
        in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> framePayload,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
