using Common.MC;
using GameServer.Application;
using GameServer.Core.Dispatch.Handlers;
using GameServer.Core.Session;
using GameServer.Movement;
using GameServer.World;

namespace GameServer.Core.Dispatch;

public static class M1PlayDispatchBootstrap
{
    public const int C2S_TeleportConfirm = Protocol340Ids.PlayC2S.TeleportConfirm;
    public const int C2S_ClientSettings = Protocol340Ids.PlayC2S.ClientSettings;
    public const int C2S_KeepAlive = Protocol340Ids.PlayC2S.KeepAlive;
    public const int C2S_Player = Protocol340Ids.PlayC2S.Player;
    public const int C2S_PlayerPosition = Protocol340Ids.PlayC2S.PlayerPosition;
    public const int C2S_PlayerPositionAndLook = Protocol340Ids.PlayC2S.PlayerPositionAndLook;
    public const int C2S_PlayerLook = Protocol340Ids.PlayC2S.PlayerLook;
    public const int C2S_HeldItemChange = Protocol340Ids.PlayC2S.HeldItemChange;
    public const int C2S_PlayerDigging = Protocol340Ids.PlayC2S.PlayerDigging;
    public const int C2S_PlayerBlockPlacement = Protocol340Ids.PlayC2S.PlayerBlockPlacement;
    public const int C2S_Animation = Protocol340Ids.PlayC2S.Animation;

    public static PlayPacketDispatcher Build(
        ChunkProvider chunkProvider,
        SessionRegistry sessions,
        ChunkStreamService? chunkStream = null,
        GameServerOptions? options = null,
        PlayerMovementService? movement = null)
    {
        var effectiveOptions = options ?? GameServerOptions.CreateDefault();
        HandlerContext.Initialize(
            chunkProvider,
            sessions,
            chunkStream ?? new ChunkStreamService(chunkProvider),
            effectiveOptions,
            movement ?? new PlayerMovementService(chunkProvider));

        var dispatcher = new PlayPacketDispatcher();

        dispatcher.Register(GameSessionState.New, C2S_ClientSettings, ClientSettingsHandler.Handle);

        dispatcher.Register(GameSessionState.Play, C2S_TeleportConfirm, TeleportConfirmHandler.Handle);
        dispatcher.Register(GameSessionState.Play, C2S_ClientSettings, ClientSettingsHandler.Handle);
        dispatcher.Register(GameSessionState.Play, C2S_KeepAlive, KeepAliveHandler.Handle);
        dispatcher.Register(GameSessionState.Play, C2S_Player, MovementHandlers.HandlePlayer);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerPosition, MovementHandlers.HandlePlayerPosition);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerPositionAndLook, MovementHandlers.HandlePlayerPositionAndLook);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerLook, MovementHandlers.HandlePlayerLook);
        dispatcher.Register(GameSessionState.Play, C2S_HeldItemChange, HeldItemChangeHandler.Handle);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerDigging, BlockInteractionHandlers.HandlePlayerDigging);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerBlockPlacement, BlockInteractionHandlers.HandlePlayerBlockPlacement);
        dispatcher.Register(GameSessionState.Play, C2S_Animation, BlockInteractionHandlers.HandleAnimation);

        return dispatcher;
    }
}
