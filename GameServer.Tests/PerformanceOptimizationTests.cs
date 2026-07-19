using System.Net;
using System.Net.Sockets;
using GameServer.Application;
using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Players;
using GameServer.Replication;
using GameServer.Tick;
using GameServer.Tick.Stages;
using Xunit;

namespace GameServer.Tests;

public class PerformanceOptimizationTests
{
    [Fact]
    public async Task InputCollectStage_LeavesFramesQueuedAfterPerPlayerBudget()
    {
        using var socket = CreateSocket();
        var sessions = new SessionRegistry();
        var session = sessions.Create(socket);
        session.State = GameSessionState.Play;
        session.PlayerName = "BudgetPlayer";

        var handled = 0;
        var dispatcher = new PlayPacketDispatcher();
        dispatcher.Register(GameSessionState.Play, 0x01, Handle);
        for (var i = 0; i < 5; i++)
            session.EnqueueInput(0x01, new byte[] { 1, 1 });

        var options = GameServerOptions.CreateDefault() with { MaxInputFramesPerTick = 2 };
        var stage = new InputCollectStage(sessions, dispatcher, options);
        await stage.ExecuteAsync(1, CancellationToken.None);

        Assert.Equal(2, handled);
        Assert.Equal(3, session.IncomingCount);

        await stage.ExecuteAsync(2, CancellationToken.None);
        Assert.Equal(4, handled);
        Assert.Equal(1, session.IncomingCount);
        return;

        ValueTask Handle(
            SessionContext currentSession,
            in RuntimeLogContext context,
            in PlayPacketRouteKey route,
            ReadOnlyMemory<byte> frame,
            CancellationToken cancellationToken)
        {
            handled++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task NetworkFlushStage_QueuesPooledBatchWithoutWritingSocket()
    {
        var (registry, session, client, listener) = await CreateConnectedSessionAsync();
        using (session.Socket)
        using (client)
        using (session.Stream)
        {
            try
            {
                var payload = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
                session.EnqueueOutput(payload);
                var stage = new NetworkFlushStage(
                    registry,
                    GameServerOptions.CreateDefault() with { MaxNetworkBatchBytes = 1024 });

                await stage.ExecuteAsync(1, CancellationToken.None);

                Assert.Equal(0, session.OutgoingBytes);
                Assert.Equal(payload.Length, session.PendingSendBytes);
                Assert.True(session.TryReadSendBatch(out var batch));
                Assert.NotNull(batch);
                Assert.Equal(payload, batch!.Memory.ToArray());
                session.ReleaseSendBatch(batch);
                Assert.Equal(0, session.PendingSendBytes);
                Assert.True(batch.IsDisposed);
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    [Fact]
    public async Task NetworkFlushStage_BoundsBytesMovedPerTick()
    {
        var (registry, session, client, listener) = await CreateConnectedSessionAsync();
        using (session.Socket)
        using (client)
        using (session.Stream)
        {
            try
            {
                session.EnqueueOutput(new byte[10_000]);
                var stage = new NetworkFlushStage(
                    registry,
                    GameServerOptions.CreateDefault() with
                    {
                        MaxNetworkBatchBytes = 4096,
                        MaxPendingSendBytes = 32_000
                    });

                await stage.ExecuteAsync(1, CancellationToken.None);

                Assert.Equal(4096, session.PendingSendBytes);
                Assert.Equal(10_000 - 4096, session.OutgoingBytes);
                Assert.True(session.TryReadSendBatch(out var batch));
                Assert.Equal(4096, batch!.Length);
                session.ReleaseSendBatch(batch);
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    [Fact]
    public void SessionContext_RejectsBatchWhenSendBacklogWouldExceedLimit()
    {
        using var socket = CreateSocket();
        var session = new SessionContext(1, socket);
        var first = OutboundBatch.Rent(1024);
        first.SetLength(1024);
        Assert.True(session.TryQueueSendBatch(first, 1024));

        var rejected = OutboundBatch.Rent(1);
        rejected.SetLength(1);
        Assert.False(session.TryQueueSendBatch(rejected, 1024));
        rejected.Dispose();

        Assert.True(session.TryReadSendBatch(out var queued));
        session.ReleaseSendBatch(queued!);
        Assert.Equal(0, session.PendingSendBytes);
    }

    [Fact]
    public void EntityTracker_ReusesObserverScratchCollections()
    {
        using var socket1 = CreateSocket();
        using var socket2 = CreateSocket();
        var observer = CreatePlaySession(1, socket1, entityId: 10, x: 0);
        var other = CreatePlaySession(2, socket2, entityId: 20, x: 1);
        var sessions = new List<SessionContext> { observer, other };
        var lookup = sessions.ToDictionary(s => s.Player!.EntityId, s => s.Player!);
        var tracker = new EntityTracker();

        var first = tracker.UpdateVisibility(observer, sessions, lookup);
        var spawned = first.Spawned;
        var despawned = first.Despawned;
        var moved = first.Moved;

        var second = tracker.UpdateVisibility(observer, sessions, lookup);

        Assert.Same(spawned, second.Spawned);
        Assert.Same(despawned, second.Despawned);
        Assert.Same(moved, second.Moved);
    }

    [Fact]
    public void TickMetrics_ComputesRollingPercentilesAndStageAggregates()
    {
        var metrics = new TickMetrics(windowSize: 100);
        for (var i = 1; i <= 100; i++)
        {
            metrics.EndTick(i);
            metrics.RecordStage(TickPipelineStage.InputCollect,
                System.Diagnostics.Stopwatch.Frequency / 1000, allocatedBytes: 10);
            metrics.AddInputFrames(2);
        }

        var snapshot = metrics.TakeSnapshotAndResetInterval();

        Assert.Equal(50, snapshot.P50Ms);
        Assert.Equal(95, snapshot.P95Ms);
        Assert.Equal(99, snapshot.P99Ms);
        Assert.Equal(1, snapshot.InputCollect.AverageMs, precision: 3);
        Assert.Equal(10, snapshot.InputCollect.AverageAllocatedBytes);
        Assert.Equal(200, snapshot.InputFrames);
    }

    private static async Task<(SessionRegistry Registry, SessionContext Session, Socket Client, TcpListener Listener)>
        CreateConnectedSessionAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var connectTask = client.ConnectAsync(endpoint);
        var serverSocket = await listener.AcceptSocketAsync();
        await connectTask;

        var registry = new SessionRegistry();
        var session = registry.Create(serverSocket);
        session.State = GameSessionState.Play;
        session.PlayerName = "NetworkPlayer";
        session.Stream = new NetworkStream(serverSocket, ownsSocket: false);
        return (registry, session, client, listener);
    }

    private static SessionContext CreatePlaySession(
        long sessionId,
        Socket socket,
        int entityId,
        double x)
    {
        return new SessionContext(sessionId, socket)
        {
            State = GameSessionState.Play,
            Player = new PlayerContext
            {
                EntityId = entityId,
                PlayerId = Guid.NewGuid().ToString("N"),
                PlayerName = $"Player{entityId}",
                X = x,
                Y = 4,
                Z = 0
            }
        };
    }

    private static Socket CreateSocket()
        => new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
}
