using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameServer.Application;
using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Network;
using GameServer.Players;
using GameServer.Persistence;
using GameServer.Replication;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Network.Backend;

public sealed class BackendGatewayServer
{
    private readonly PlayPacketDispatcher _dispatcher;
    private readonly GameServerOptions _options;
    private readonly SessionRegistry _sessions;
    private readonly PlayerJoinFlow _joinFlow;
    private readonly EntityTracker _entityTracker;
    private readonly PlayerManager _playerManager;
    private readonly IPlayerDataStore _playerDataStore;
    private readonly ConcurrentDictionary<long, Task> _sessionLoops = new();

    private TcpListener? _listener;

    public BackendGatewayServer(
        GameServerOptions options,
        SessionRegistry sessions,
        PlayPacketDispatcher dispatcher,
        PlayerJoinFlow joinFlow,
        EntityTracker entityTracker,
        PlayerManager playerManager,
        IPlayerDataStore playerDataStore)
    {
        _options = options;
        _sessions = sessions;
        _dispatcher = dispatcher;
        _joinFlow = joinFlow;
        _entityTracker = entityTracker;
        _playerManager = playerManager;
        _playerDataStore = playerDataStore;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(_options.GatewayBackendListenEndPoint);
        _listener.Start();
        Info("BackendGateway", $"Listening on {_options.GatewayBackendListenEndPoint}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket socket;
                try
                {
                    socket = await _listener.AcceptSocketAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!IsLoopback(socket))
                {
                    Warn("BackendGateway", $"Rejected non-loopback connection: {socket.RemoteEndPoint}");
                    SafeClose(socket);
                    continue;
                }

                socket.NoDelay = true;

                var session = _sessions.Create(socket);
                var loopTask = RunSessionLoopAsync(session, cancellationToken);
                _sessionLoops.TryAdd(session.SessionId, loopTask);
                _ = loopTask.ContinueWith(
                    _ =>
                    {
                        _sessionLoops.TryRemove(session.SessionId, out Task? _);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        finally
        {
            _listener.Stop();
            _listener = null;
            await DrainSessionsAsync();
        }
    }

    private async Task RunSessionLoopAsync(SessionContext session, CancellationToken cancellationToken)
    {
        Info("Session", session.SessionId, $"Session opened, remote={session.Socket.RemoteEndPoint}");

        try
        {
            using var stream = new NetworkStream(session.Socket, ownsSocket: false);
            session.Stream = stream;

            // Read Gateway handshake to identify the player before join sequence.
            await ReadGatewayHandshakeAsync(stream, session, cancellationToken);

            session.State = GameSessionState.Joining;

            await _joinFlow.ExecuteAsync(session, cancellationToken);

            Info("Session", session.SessionId, "Entering read loop, enqueuing to tick pipeline");

            // Start periodic KeepAlive sender — client disconnects after ~30s without one.
            // In v0.3.2, KeepAlive frames are enqueued to the tick pipeline's output queue,
            // flushed by NetworkFlushStage (single writer, no concurrency risk).
            var keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var keepAliveTask = RunKeepAliveLoopAsync(session, keepAliveCts.Token);

            try
            {
                while (!cancellationToken.IsCancellationRequested && !session.Closed)
                {
                    var frame = await McPlayFrameCodec.ReadFrameAsync(stream, cancellationToken);
                    if (frame == null)
                        break;

                    session.Touch();
                    if (!McPlayFrameCodec.TryGetPacketId(frame, out var packetId))
                    {
                        Warn("Session", session.SessionId, $"Invalid frame, disconnecting");
                        break;
                    }

                    // Fast path: only deserialize and enqueue. All game logic runs in the tick loop.
                    session.EnqueueInput(packetId, frame);
                }
            }
            finally
            {
                keepAliveCts.Cancel();
                await keepAliveTask;
                keepAliveCts.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Error("Session", session.SessionId, $"Session loop error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (session.TryMarkClosed())
            {
                _sessions.Remove(session.SessionId, out _);
                SafeClose(session.Socket);

                // M3: Broadcast entity removal to remaining players
                if (session.Player != null)
                {
                    var entityId = session.Player.EntityId;
                    var playerId = session.Player.PlayerId;
                    try
                    {
                        await _playerDataStore.SaveAsync(PlayerStatePersistence.Capture(session.Player), CancellationToken.None);
                        Info("PlayerPersistence", session.SessionId, $"Saved player state for {playerId}");
                    }
                    catch (Exception ex)
                    {
                        Error("PlayerPersistence", session.SessionId,
                            $"Could not save player state: {ex.GetType().Name}: {ex.Message}");
                    }

                    _entityTracker.RemoveEntity(entityId);
                    _entityTracker.RemoveObserver(session.SessionId);
                    _playerManager.RemovePlayer(entityId, out _);

                    var destroyPkt = S2CPacketBuilders.BuildDestroyEntities(new[] { entityId });
                    var listRemove = S2CPacketBuilders.BuildPlayerListItemRemove(playerId);
                    foreach (var (otherSid, other) in _sessions.All)
                    {
                        if (other.State == GameSessionState.Play && !other.Closed)
                        {
                            other.EnqueueOutput(destroyPkt);
                            other.EnqueueOutput(listRemove);
                        }
                    }
                }

                Info("Session", session.SessionId, $"Session closed");
            }
        }
    }

    private static async Task RunKeepAliveLoopAsync(SessionContext session, CancellationToken ct)
    {
        var keepAliveId = (long)(DateTime.UtcNow.Ticks & 0x7FFFFFFFFFFFFFFF);
        // Send first KeepAlive after 2s, then every 5s.
        // Vanilla client times out after ~30s of no KeepAlive.
        const int firstDelayMs = 2000;
        const int intervalMs = 5000;

        try
        {
            await Task.Delay(firstDelayMs, ct);

            while (!ct.IsCancellationRequested && !session.Closed)
            {
                var frame = S2CPacketBuilders.BuildKeepAlive(keepAliveId);
                // Enqueue via tick pipeline — flushed by NetworkFlushStage (single writer, no concurrency risk)
                session.EnqueueOutput(frame);
                Info("KeepAlive", session.SessionId, $"S2C KeepAlive enqueued, id={keepAliveId}");
                keepAliveId++;

                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainSessionsAsync()
    {
        var tasks = _sessionLoops.Values.ToArray();
        if (tasks.Length == 0)
            return;

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }
    }

    private static bool IsLoopback(Socket socket)
    {
        if (socket.RemoteEndPoint is not IPEndPoint endPoint)
            return false;

        return IPAddress.IsLoopback(endPoint.Address);
    }

    private static void SafeClose(Socket socket)
    {
        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            socket.Close();
        }
        catch
        {
        }
    }

    /// <summary>
    /// Reads the PlayerSessionOpen handshake frame from Gateway.
    /// Format: [VarInt frameLength] [u8 msgType=0x01] [string playerUuid] [string playerName]
    /// Sets session.PlayerId so the join flow can identify the player.
    /// </summary>
    private static async Task ReadGatewayHandshakeAsync(
        NetworkStream stream, SessionContext session, CancellationToken ct)
    {
        // Read VarInt frame length byte-by-byte
        var lenBuf = new byte[5];
        var lenRead = 0;
        var singleByte = new byte[1];
        for (var i = 0; i < 5; i++)
        {
            var n = await stream.ReadAsync(singleByte.AsMemory(), ct);
            if (n == 0) throw new EndOfStreamException("Connection closed before handshake");
            lenBuf[i] = singleByte[0];
            lenRead++;
            if ((singleByte[0] & 0x80) == 0) break;
        }

        var lenOff = 0;
        if (!McPlayFrameCodec.TryReadVarInt(lenBuf.AsSpan(0, lenRead), ref lenOff, out var frameLen)
            || frameLen < 1 || frameLen > 1024)
            throw new InvalidOperationException($"Invalid handshake frame length: {frameLen}");

        // Read frame body
        var body = new byte[frameLen];
        var totalRead = 0;
        while (totalRead < frameLen)
        {
            var n = await stream.ReadAsync(body.AsMemory(totalRead), ct);
            if (n == 0) throw new EndOfStreamException("Connection closed during handshake body");
            totalRead += n;
        }

        var off = 0;
        var span = body.AsSpan();

        // msgType (u8)
        if (off >= span.Length) throw new InvalidOperationException("Handshake too short for msgType");
        var msgType = span[off++];
        if (msgType != 0x01)
            throw new InvalidOperationException($"Expected PlayerSessionOpen (0x01), got 0x{msgType:X2}");

        // playerUuid (string)
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var uuidLen)
            || uuidLen < 0 || uuidLen > 64 || off + uuidLen > span.Length)
            throw new InvalidOperationException("Invalid handshake uuid");

        var playerUuid = Encoding.UTF8.GetString(span.Slice(off, uuidLen));
        off += uuidLen;

        // playerName (string)
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var nameLen)
            || nameLen < 0 || nameLen > 32 || off + nameLen > span.Length)
            throw new InvalidOperationException("Invalid handshake playerName");

        var playerName = Encoding.UTF8.GetString(span.Slice(off, nameLen));

        session.PlayerId = playerUuid;
        session.PlayerName = playerName;

        Info("BackendGateway", session.SessionId,
            $"Gateway handshake complete, playerId={playerUuid}, playerName={playerName}");
    }

}
