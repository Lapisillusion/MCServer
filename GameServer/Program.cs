using GameServer.Application;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Dimension;
using GameServer.Network;
using GameServer.Network.Backend;
using GameServer.Players;
using GameServer.Persistence;
using GameServer.Replication;
using GameServer.Tick;
using GameServer.Tick.Stages;
using GameServer.World;
using Serilog;
using Serilog.Events;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "GameServer")
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/gameserver-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.Seq("http://localhost:5341")
            .CreateLogger();

        try
        {
            var m0 = M0Bootstrapper.Initialize();
            var options = GameServerOptions.CreateDefault();
            var sessions = new SessionRegistry();

            // v0.2.0 — Spawn Pipeline dependencies
            var dimensionManager = new DimensionManager();
            dimensionManager.RegisterDefaults();
            var spawnManager = new SpawnManager();
            var playerManager = new PlayerManager();
            var playerDataStore = new FilePlayerDataStore(options.PlayerDataDirectory);

            // v0.3.0 — Entity tracking for multiplayer visibility
            var entityTracker = new EntityTracker();
            var joinFlow = new PlayerJoinFlow(playerManager, spawnManager, dimensionManager, options, sessions, entityTracker, playerDataStore);

            // v0.2.1 — Chunk Provider (shared storage for generated chunks)
            var chunkProvider = new ChunkProvider();

            var chunkStream = new ChunkStreamService(chunkProvider);
            var playDispatcher = M1PlayDispatchBootstrap.Build(chunkProvider, sessions, chunkStream, options);
            var server = new BackendGatewayServer(options, sessions, playDispatcher, joinFlow, entityTracker, playerManager, playerDataStore);

            // v0.3.2 — Tick Pipeline stages (InputCollect → Simulate → Replication → NetworkFlush)
            var inputCollect = new InputCollectStage(sessions, playDispatcher);
            var liveness = new SessionLivenessService(options);
            var simulate = new SimulateStage(sessions, liveness, chunkStream);
            var replication = new ReplicationStage(sessions, entityTracker);
            var networkFlush = new NetworkFlushStage(sessions, options);

            var tickScheduler = new TickScheduler(new ITickStage[]
            {
                inputCollect,
                simulate,
                replication,
                networkFlush
            });

            Info("Startup",
                $"M0 Ready — InternalMessages={m0.InternalMessageCount}, DispatchRoutes={m0.DispatchRouteCount}, TickPhases={m0.TickPhaseCount}");
            Info("Startup",
                $"M1+M2+M3 Ready — Listen={options.GatewayBackendListenEndPoint}, PlayDispatchRoutes={playDispatcher.RouteCount}, TickStages=4");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            // Run server accept loop and 20 TPS tick loop in parallel.
            var serverTask = server.RunAsync(cts.Token);
            var tickTask = tickScheduler.RunAsync(cts.Token);
            await Task.WhenAll(serverTask, tickTask);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "GameServer terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
