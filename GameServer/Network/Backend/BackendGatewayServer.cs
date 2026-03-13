using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GameServer.Application;
using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;

namespace GameServer.Network.Backend;

public sealed class BackendGatewayServer
{
    private readonly PlayPacketDispatcher _dispatcher;
    private readonly GameServerOptions _options;
    private readonly SessionRegistry _sessions;
    private readonly ConcurrentDictionary<long, Task> _sessionLoops = new();

    private TcpListener? _listener;

    public BackendGatewayServer(
        GameServerOptions options,
        SessionRegistry sessions,
        PlayPacketDispatcher dispatcher)
    {
        _options = options;
        _sessions = sessions;
        _dispatcher = dispatcher;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(_options.GatewayBackendListenEndPoint);
        _listener.Start();
        Log($"Listening Gateway backend on {_options.GatewayBackendListenEndPoint}");

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
                    Log($"Rejected non-loopback backend connection: {socket.RemoteEndPoint}");
                    SafeClose(socket);
                    continue;
                }

                socket.NoDelay = true;

                var session = _sessions.Create(socket);
                session.State = GameSessionState.Play;
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
        Log($"Session opened: sessionId={session.SessionId}, remote={session.Socket.RemoteEndPoint}");

        try
        {
            using var stream = new NetworkStream(session.Socket, ownsSocket: false);
            while (!cancellationToken.IsCancellationRequested && !session.Closed)
            {
                var frame = await McPlayFrameCodec.ReadFrameAsync(stream, cancellationToken);
                if (frame == null)
                    break;

                session.Touch();
                if (!McPlayFrameCodec.TryGetPacketId(frame, out var packetId))
                {
                    Log($"Invalid frame from session={session.SessionId}, disconnect.");
                    break;
                }

                var context = RuntimeLogContext.Empty
                    .WithSessionId(session.SessionId.ToString())
                    .WithPlayerId(session.PlayerId)
                    .WithPacketId($"0x{packetId:X2}")
                    .WithTickId(-1);

                await _dispatcher.DispatchOrIgnoreAsync(session, packetId, context, frame, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log($"Session loop error: sessionId={session.SessionId}, ex={ex.GetType().Name}, msg={ex.Message}");
        }
        finally
        {
            if (session.TryMarkClosed())
            {
                _sessions.Remove(session.SessionId, out _);
                SafeClose(session.Socket);
                Log($"Session closed: sessionId={session.SessionId}");
            }
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

    private static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.UtcNow:O}] [BackendGateway] {message}");
    }
}
