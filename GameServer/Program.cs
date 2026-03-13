using GameServer.Application;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Network.Backend;

namespace GameServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var m0 = M0Bootstrapper.Initialize();
        var options = GameServerOptions.CreateDefault();
        var sessions = new SessionRegistry();
        var playDispatcher = M1PlayDispatchBootstrap.Build();
        var server = new BackendGatewayServer(options, sessions, playDispatcher);

        Console.WriteLine(
            $"[M0] Ready. InternalMessages={m0.InternalMessageCount}, DispatchRoutes={m0.DispatchRouteCount}, TickPhases={m0.TickPhaseCount}");
        Console.WriteLine(
            $"[M1] Ready. Listen={options.GatewayBackendListenEndPoint}, PlayDispatchRoutes={playDispatcher.RouteCount}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        await server.RunAsync(cts.Token);
    }
}
