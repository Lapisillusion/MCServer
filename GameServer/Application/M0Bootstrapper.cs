using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Network.Internal;
using GameServer.Tick;

namespace GameServer.Application;

public readonly record struct M0BootstrapBaseline(
    int InternalMessageCount,
    int DispatchRouteCount,
    int TickPhaseCount);

public static class M0Bootstrapper
{
    public static M0BootstrapBaseline Initialize()
    {
        var catalog = InternalMessageCatalog.CreateDefault();
        var dispatcher = M0DispatchBootstrap.Build();

        ValidateRequiredInboundRoutes(dispatcher);

        var context = RuntimeLogContext.Empty
            .WithSessionId("bootstrap")
            .WithPlayerId("system")
            .WithPacketId("M0");
        _ = context.WithTickId(0);

        return new M0BootstrapBaseline(
            InternalMessageCount: catalog.Count,
            DispatchRouteCount: dispatcher.RouteCount,
            TickPhaseCount: TickPipelineOrder.Default.Count);
    }

    private static void ValidateRequiredInboundRoutes(InternalMessageDispatcher dispatcher)
    {
        var required = new[]
        {
            new DispatchRouteKey(GameSessionState.New, InternalMessageType.PlayerSessionOpen),
            new DispatchRouteKey(GameSessionState.Play, InternalMessageType.PlayerPlayPacket),
            new DispatchRouteKey(GameSessionState.Play, InternalMessageType.PlayerMoveInput),
            new DispatchRouteKey(GameSessionState.Play, InternalMessageType.PlayerActionInput),
            new DispatchRouteKey(GameSessionState.New, InternalMessageType.PlayerSessionClose),
            new DispatchRouteKey(GameSessionState.Play, InternalMessageType.PlayerSessionClose),
            new DispatchRouteKey(GameSessionState.Closing, InternalMessageType.PlayerSessionClose)
        };

        foreach (var route in required)
        {
            if (!dispatcher.ContainsRoute(route))
            {
                throw new InvalidOperationException(
                    $"M0 baseline invalid. Missing route: state={route.State}, messageType={route.MessageType}");
            }
        }
    }
}
