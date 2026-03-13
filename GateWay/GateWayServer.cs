// GatewayServer.cs

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Common;
using Serilog;

namespace GateWay;

public sealed class GateWayServer
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<GateWayServer>();

    private const int AcceptPoolInitial = 256;
    private const int AcceptPoolMax = 2048;
    private const int IoPoolInitial = 4096;
    private const int IoPoolMax = 50000;
    private const int IoTokenPoolInitial = IoPoolInitial + AcceptPoolInitial;
    private const int IoTokenPoolMax = IoPoolMax + AcceptPoolMax;
    private const int IoBufferSize = 32 * 1024;
    private const int InvalidPacketBanThreshold = 3;
    private const int LoginInProgressLimit = 2000;
    private static readonly TimeSpan TempBanDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(5);
    private const string StatusResponseFileName = "status-response.json";
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "server",
        "console",
        "system"
    };
    private static readonly byte[] StatusResponseFrame = BuildStatusResponseFrame();

    private readonly IPEndPoint _gameServerEp;
    private readonly Blacklist _blacklist;
    private readonly RateLimiter _limiter;
    private readonly IPEndPoint _listenEp;
    private readonly SaeaPool _acceptPool;
    private readonly ConcurrentDictionary<int, ConnectionContext> _contexts = new();
    private readonly SaeaPool _ioPool;
    private readonly ObjectPool<IoToken> _ioTokenPool;
    private readonly double _ticksPerSec = Stopwatch.Frequency;

    private Socket _listen = null!;
    private int _loginInProgress;
    private int _nextContextId;
    private Timer? _timeoutTimer;

    public GateWayServer(GateWayOptions options, Blacklist blacklist, RateLimiter limiter)
    {
        _listenEp = options.ListenEndPoint;
        _gameServerEp = options.GameServerEndPoint;
        _blacklist = blacklist;
        _limiter = limiter;

        _acceptPool = new SaeaPool(AcceptPoolInitial, AcceptPoolMax, 0, OnIoCompleted);
        _ioPool = new SaeaPool(IoPoolInitial, IoPoolMax, IoBufferSize, OnIoCompleted);
        _ioTokenPool = new ObjectPool<IoToken>(
            IoTokenPoolInitial,
            IoTokenPoolMax,
            () => new IoToken(),
            token =>
            {
                token.Operation = IoOperation.Accept;
                token.Context = null;
            });
    }

    public void Start()
    {
        _listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listen.NoDelay = true;
        _listen.Bind(_listenEp);
        _listen.Listen(8192);

        _timeoutTimer = new Timer(CheckTimeouts, null, 1000, 1000);
        StartAccept();
        Logger.Information("Gateway listening on {ListenEndPoint}, fixed GameServer backend={BackendEndPoint}", _listenEp,
            _gameServerEp);
    }

    private void OnIoCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is not IoToken token)
            return;

        switch (token.Operation)
        {
            case IoOperation.Accept:
                HandleAccept(e);
                break;
            case IoOperation.ClientRecv:
                HandleClientRecv(e, token.Context!);
                break;
            case IoOperation.BackendRecv:
                HandleBackendRecv(e, token.Context!);
                break;
            case IoOperation.BackendSend:
                HandleSendCompleted(e, token.Context!, ctx => ctx.BackendSend, GetBackendSocketOrThrow, "backend-send");
                break;
            case IoOperation.ClientSend:
                HandleSendCompleted(e, token.Context!, ctx => ctx.ClientSend, ctx => ctx.Client, "client-send");
                break;
        }
    }

    private void StartAccept()
    {
        if (!_acceptPool.TryRent(out var saea))
        {
            Logger.Debug("Accept pool exhausted, scheduling retry");
            ThreadPool.QueueUserWorkItem(_ => StartAccept());
            return;
        }

        saea.AcceptSocket = null;
        if (!TryRentIoToken(saea, IoOperation.Accept, null))
        {
            _acceptPool.Return(saea);
            Logger.Warning("IoToken pool exhausted for Accept, scheduling retry");
            ThreadPool.QueueUserWorkItem(_ => StartAccept());
            return;
        }

        if (!_listen.AcceptAsync(saea))
            HandleAccept(saea);
    }

    private void HandleAccept(SocketAsyncEventArgs e)
    {
        var client = e.AcceptSocket;
        ReturnIoToken(e);
        _acceptPool.Return(e);

        // Always keep accept loop hot.
        StartAccept();

        if (client == null)
            return;

        try
        {
            client.NoDelay = true;

            var remote = (IPEndPoint)client.RemoteEndPoint!;
            if (remote.AddressFamily != AddressFamily.InterNetwork)
            {
                Logger.Debug("Rejected non-IPv4 client, remote={RemoteEndPoint}", remote);
                client.Close();
                return;
            }

            var ip = IpUtils.ToUInt32(remote.Address);
            if (_blacklist.IsBlocked(ip))
            {
                Logger.Warning("Rejected blocked client, ip={Ip}", FormatIpv4(ip));
                client.Close();
                return;
            }

            if (!_limiter.TryAcceptConnection(ip))
            {
                Logger.Warning("Rejected by connection rate limiter, ip={Ip}", FormatIpv4(ip));
                client.Close();
                return;
            }

            var ctx = new ConnectionContext
            {
                Client = client,
                IpV4 = ip,
                State = ConnState.Handshake,
                LastActivityTicks = Stopwatch.GetTimestamp()
            };

            if (!TryRentConnectionSaeas(ctx))
            {
                Logger.Warning("Failed to allocate IO resources for new connection, ip={Ip}", FormatIpv4(ip));
                CloseContext(ctx, false, "io-resources-exhausted");
                return;
            }

            var id = Interlocked.Increment(ref _nextContextId);
            ctx.Id = id;
            _contexts[id] = ctx;

            Logger.Debug("Connection accepted, ctxId={ContextId}, ip={Ip}, remote={RemoteEndPoint}", id, FormatIpv4(ip), remote);

            StartReceive(ctx.Client, ctx.ClientRecvSaea);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HandleAccept failed unexpectedly");
            SafeClose(client);
        }
    }

    private bool TryRentConnectionSaeas(ConnectionContext ctx)
    {
        if (!TryRentIo(ctx, IoOperation.ClientRecv, out var cRecv))
            return false;

        if (!TryRentIo(ctx, IoOperation.ClientSend, out var cSend))
        {
            _ioPool.Return(cRecv);
            return false;
        }

        ctx.ClientRecvSaea = cRecv;
        ctx.ClientSendSaea = cSend;
        return true;
    }

    private bool TryAttachGameServerBackend(ConnectionContext ctx, out string failReason)
    {
        failReason = string.Empty;
        if (ctx.HasBackend)
            return true;

        Socket? backend = null;
        try
        {
            backend = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            backend.NoDelay = true;
            backend.Connect(_gameServerEp);

            if (!TryRentIo(ctx, IoOperation.BackendRecv, out var backendRecv))
            {
                failReason = "backend-recv-io-exhausted";
                SafeClose(backend);
                return false;
            }

            if (!TryRentIo(ctx, IoOperation.BackendSend, out var backendSend))
            {
                ReturnSaea(backendRecv);
                failReason = "backend-send-io-exhausted";
                SafeClose(backend);
                return false;
            }

            ctx.Backend = backend;
            ctx.BackendRecvSaea = backendRecv;
            ctx.BackendSendSaea = backendSend;

            StartReceive(backend, backendRecv);
            Logger.Debug("GameServer backend attached, ctxId={ContextId}, backend={Backend}",
                ctx.Id, _gameServerEp);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "GameServer backend connect failed, ctxId={ContextId}, backend={Backend}",
                ctx.Id, _gameServerEp);

            if (backend != null)
                SafeClose(backend);

            if (ctx.BackendRecvSaea != null)
            {
                ReturnSaea(ctx.BackendRecvSaea);
                ctx.BackendRecvSaea = null;
            }

            if (ctx.BackendSendSaea != null)
            {
                ReturnSaea(ctx.BackendSendSaea);
                ctx.BackendSendSaea = null;
            }

            ctx.Backend = null;
            failReason = "backend-connect-failed";
            return false;
        }
    }

    private bool TryRentIo(ConnectionContext ctx, IoOperation op, out SocketAsyncEventArgs saea)
    {
        if (!_ioPool.TryRent(out saea!))
        {
            Logger.Debug("IO SAEA pool exhausted, ctxId={ContextId}, operation={Operation}", ctx.Id, op);
            return false;
        }

        if (!TryRentIoToken(saea, op, ctx))
        {
            _ioPool.Return(saea);
            Logger.Debug("IoToken pool exhausted, ctxId={ContextId}, operation={Operation}", ctx.Id, op);
            return false;
        }

        return true;
    }

    private bool TryRentIoToken(SocketAsyncEventArgs saea, IoOperation op, ConnectionContext? ctx)
    {
        if (!_ioTokenPool.TryRent(out var token))
        {
            Logger.Warning("IoToken pool exhausted, operation={Operation}", op);
            return false;
        }

        token.Operation = op;
        token.Context = ctx;
        saea.UserToken = token;
        return true;
    }

    private void ReturnIoToken(SocketAsyncEventArgs saea)
    {
        if (saea.UserToken is not IoToken token)
            return;

        saea.UserToken = null;
        token.Context = null;
        _ioTokenPool.Return(token);
    }

    private void StartReceive(Socket socket, SocketAsyncEventArgs saea)
    {
        if (!socket.ReceiveAsync(saea))
            OnIoCompleted(socket, saea);
    }

    private void HandleClientRecv(SocketAsyncEventArgs e, ConnectionContext ctx)
    {
        if (ctx.Closed)
            return;

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            Logger.Debug("Client recv closed, ctxId={ContextId}, socketError={SocketError}, bytes={Bytes}",
                ctx.Id, e.SocketError, e.BytesTransferred);
            CloseContext(ctx, false, "client-recv-closed");
            return;
        }

        ctx.LastActivityTicks = Stopwatch.GetTimestamp();

        try
        {
            lock (ctx.ParserSync)
            {
                ctx.ClientInbound.Write(e.Buffer.AsSpan(e.Offset, e.BytesTransferred));
                ProcessClientFrames(ctx);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Client frame processing failed, ctxId={ContextId}", ctx.Id);
            RegisterInvalidPacketAndClose(ctx);
            return;
        }

        if (!ctx.Closed)
            StartReceive(ctx.Client, e);
    }
    private void ProcessClientFrames(ConnectionContext ctx)
    {
        Span<byte> peek5 = stackalloc byte[5];

        while (true)
        {
            if (!TryReadClientFrame(ctx, peek5, out var frameLen))
                return;

            if (!ctx.ClientInbound.TryGetReadSegments(ctx.ParsedClientBytes, frameLen, out var seg1, out var seg2))
                return;

            byte[]? parseRent = null;
            ReadOnlySpan<byte> frameSpan;
            if (seg2.Count == 0)
            {
                frameSpan = seg1.AsSpan();
            }
            else
            {
                parseRent = ArrayPool<byte>.Shared.Rent(frameLen);
                seg1.AsSpan().CopyTo(parseRent.AsSpan(0, seg1.Count));
                seg2.AsSpan().CopyTo(parseRent.AsSpan(seg1.Count, seg2.Count));
                frameSpan = parseRent.AsSpan(0, frameLen);
            }

            var stateBefore = ctx.State;
            var forwardToBackend = stateBefore == ConnState.Play;
            object? parsed = null;

            try
            {
                if (!McFrameParser.TryGetPacketId(frameSpan, out var pid, out var payloadOffset))
                {
                    Logger.Warning("PacketId parse failed, ctxId={ContextId}, state={State}", ctx.Id, ctx.State);
                    RegisterInvalidPacketAndClose(ctx);
                    return;
                }

                var payloadLen = frameLen - payloadOffset;
                if (!StateMachine.IsAllowed(stateBefore, pid))
                {
                    Logger.Warning("State machine reject, ctxId={ContextId}, state={State}, packetId={PacketId}",
                        ctx.Id, stateBefore, pid);
                    RegisterInvalidPacketAndClose(ctx);
                    return;
                }

                if (stateBefore == ConnState.Handshake && pid == Protocol340Ids.C2S_Handshake)
                {
                    if (!McFrameParser.TryParseHandshake(frameSpan, out var handshake))
                    {
                        Logger.Warning("Handshake parse failed, ctxId={ContextId}", ctx.Id);
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    if (handshake.ProtocolVersion != Protocol340Ids.ProtocolVersion)
                    {
                        Logger.Warning("Protocol version reject, ctxId={ContextId}, protocolVersion={ProtocolVersion}, expected={Expected}",
                            ctx.Id, handshake.ProtocolVersion, Protocol340Ids.ProtocolVersion);
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    ctx.ProtocolVersion = handshake.ProtocolVersion;
                    parsed = new
                    {
                        PacketType = "Handshake",
                        handshake.ProtocolVersion,
                        handshake.ServerAddress,
                        handshake.ServerPort,
                        handshake.NextState
                    };
                    forwardToBackend = false;

                    if (handshake.NextState == 1)
                    {
                        ctx.State = ConnState.Status;
                        Logger.Debug("State transition, ctxId={ContextId}, from={FromState}, to={ToState}",
                            ctx.Id, ConnState.Handshake, ConnState.Status);
                    }
                    else
                    {
                        if (!TryAcquireLoginSlot(ctx))
                        {
                            Logger.Warning("Login slot exhausted, ctxId={ContextId}, ip={Ip}", ctx.Id, FormatIpv4(ctx.IpV4));
                            CloseContext(ctx, false, "login-slot-exhausted");
                            return;
                        }

                        ctx.State = ConnState.Login;
                        Logger.Debug("State transition, ctxId={ContextId}, from={FromState}, to={ToState}",
                            ctx.Id, ConnState.Handshake, ConnState.Login);
                    }
                }
                else if (stateBefore == ConnState.Status)
                {
                    if (pid == Protocol340Ids.C2S_StatusRequest)
                    {
                        if (!McFrameParser.TryIsStatusRequest(frameSpan))
                        {
                            Logger.Warning("StatusRequest validate failed, ctxId={ContextId}", ctx.Id);
                            RegisterInvalidPacketAndClose(ctx);
                            return;
                        }

                        parsed = new { PacketType = "StatusRequest" };
                        forwardToBackend = false;
                        SendStatusResponse(ctx);
                    }
                    else if (pid == Protocol340Ids.C2S_StatusPing)
                    {
                        if (!McFrameParser.TryReadStatusPingPayload(frameSpan, out var payload))
                        {
                            Logger.Warning("StatusPing parse failed, ctxId={ContextId}", ctx.Id);
                            RegisterInvalidPacketAndClose(ctx);
                            return;
                        }

                        parsed = new { PacketType = "StatusPing", Payload = payload };
                        forwardToBackend = false;
                        SendStatusPongAndClose(ctx, payload);
                    }
                }
                else if (stateBefore == ConnState.Login && pid == Protocol340Ids.C2S_LoginStart)
                {
                    if (!_limiter.TryAcceptLogin(ctx.IpV4))
                    {
                        Logger.Warning("Rejected by login rate limiter, ctxId={ContextId}, ip={Ip}", ctx.Id,
                            FormatIpv4(ctx.IpV4));
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    if (!McFrameParser.TryParseLoginStart(frameSpan, out var loginStart))
                    {
                        Logger.Warning("LoginStart validate failed, ctxId={ContextId}", ctx.Id);
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    if (ReservedNames.Contains(loginStart.Username))
                    {
                        Logger.Warning("Rejected reserved player name, ctxId={ContextId}, name={PlayerName}", ctx.Id, loginStart.Username);
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    ctx.PlayerName = loginStart.Username;
                    var uuid = BuildOfflinePlayerUuid(loginStart.Username);
                    ctx.PlayerUuid = uuid;
                    parsed = new
                    {
                        PacketType = "LoginStart",
                        PlayerName = loginStart.Username,
                        PlayerUuid = uuid
                    };
                    forwardToBackend = false;

                    if (!TryAttachGameServerBackend(ctx, out var failReason))
                    {
                        SendLoginDisconnectAndClose(ctx, "GameServer is unavailable, please retry.");
                        Logger.Warning("Login rejected due backend unavailable, ctxId={ContextId}, reason={Reason}", ctx.Id,
                            failReason);
                        return;
                    }

                    SendLoginSuccessAndPromote(ctx, loginStart.Username, uuid);
                }

                LogParsedClientPacket(ctx, stateBefore, pid, frameLen, payloadLen, parsed, forwardToBackend);

                ctx.ParsedClientBytes += frameLen;
                if (!forwardToBackend)
                {
                    ctx.ClientInbound.Skip(frameLen);
                    ctx.ParsedClientBytes -= frameLen;
                    if (ctx.ParsedClientBytes < 0)
                        ctx.ParsedClientBytes = 0;
                    continue;
                }

                EnqueueBackendZeroCopy(ctx, seg1, seg2, frameLen);
            }
            finally
            {
                if (parseRent != null)
                    ArrayPool<byte>.Shared.Return(parseRent);
            }
        }
    }
    private bool TryReadClientFrame(ConnectionContext ctx, Span<byte> temp, out int frameLenTotal)
    {
        frameLenTotal = 0;
        var available = ctx.ClientInbound.Count - ctx.ParsedClientBytes;
        if (available <= 0)
            return false;

        var peek = ctx.ClientInbound.PeekAt(ctx.ParsedClientBytes, temp, Math.Min(temp.Length, Math.Min(available, 5)));
        if (peek == 0)
            return false;

        var off = 0;
        if (!VarInt.TryRead(temp[..peek], ref off, out var payloadLen))
            return false;

        if (payloadLen < 0 || payloadLen > McFrameParser.MaxFramePayload)
            throw new InvalidOperationException("Payload too large");

        var needTotal = off + payloadLen;
        if (available < needTotal)
            return false;

        frameLenTotal = needTotal;
        return true;
    }

    private void HandleBackendRecv(SocketAsyncEventArgs e, ConnectionContext ctx)
    {
        if (ctx.Closed)
            return;
        var backend = ctx.Backend;
        if (backend == null)
        {
            Logger.Warning("Backend recv callback without backend socket, ctxId={ContextId}", ctx.Id);
            CloseContext(ctx, false, "backend-missing");
            return;
        }

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            Logger.Debug("Backend recv closed, ctxId={ContextId}, socketError={SocketError}, bytes={Bytes}",
                ctx.Id, e.SocketError, e.BytesTransferred);
            CloseContext(ctx, false, "backend-recv-closed");
            return;
        }

        ctx.LastActivityTicks = Stopwatch.GetTimestamp();
        if (ctx.State != ConnState.Play)
        {
            Logger.Debug("Drop backend packet before play, ctxId={ContextId}, state={State}, bytes={Bytes}",
                ctx.Id, ctx.State, e.BytesTransferred);
            if (!ctx.Closed)
                StartReceive(backend, e);
            return;
        }

        var rent = ArrayPool<byte>.Shared.Rent(e.BytesTransferred);
        Buffer.BlockCopy(e.Buffer!, e.Offset, rent, 0, e.BytesTransferred);

        EnqueueClientSend(ctx, rent, e.BytesTransferred);

        if (!ctx.Closed)
            StartReceive(backend, e);
    }

    private void SendLoginSuccessAndPromote(ConnectionContext ctx, string playerName, string playerUuid)
    {
        if (ctx.State != ConnState.Login)
            return;
        if (!ctx.HasBackend)
        {
            Logger.Warning("Cannot promote login without backend attached, ctxId={ContextId}", ctx.Id);
            SendLoginDisconnectAndClose(ctx, "GameServer is unavailable, please retry.");
            return;
        }

        var frame = BuildLoginSuccessFrame(playerUuid, playerName);
        var item = new SendWorkItem
        {
            Segment1 = new ArraySegment<byte>(frame, 0, frame.Length),
            Segment2 = default,
            TotalLength = frame.Length
        };

        EnqueueSend(ctx, ctx.ClientSend, item, ctx.Client, ctx.ClientSendSaea, "client-send");
        ctx.State = ConnState.Play;
        ReleaseLoginSlot(ctx);
        Logger.Information("State transition, ctxId={ContextId}, from={FromState}, to={ToState}, playerName={PlayerName}, playerUuid={PlayerUuid}",
            ctx.Id, ConnState.Login, ConnState.Play, playerName, playerUuid);
    }

    private static string BuildOfflinePlayerUuid(string playerName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{playerName}"));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // IETF variant
        return (Convert.ToHexString(hash.AsSpan(0, 4)) + "-"
               + Convert.ToHexString(hash.AsSpan(4, 2)) + "-"
               + Convert.ToHexString(hash.AsSpan(6, 2)) + "-"
               + Convert.ToHexString(hash.AsSpan(8, 2)) + "-"
               + Convert.ToHexString(hash.AsSpan(10, 6))).ToLowerInvariant();
    }

    private static byte[] BuildLoginSuccessFrame(string playerUuid, string playerName)
    {
        var uuidBytes = Encoding.UTF8.GetBytes(playerUuid.ToLowerInvariant());
        var nameBytes = Encoding.UTF8.GetBytes(playerName);
        Span<byte> varIntBuf = stackalloc byte[5];

        var uuidLenVarInt = VarInt.Write(varIntBuf, uuidBytes.Length);
        var nameLenVarInt = VarInt.Write(varIntBuf, nameBytes.Length);
        var payloadLen = 1 + uuidLenVarInt + uuidBytes.Length + nameLenVarInt + nameBytes.Length;
        var frameLenVarInt = VarInt.Write(varIntBuf, payloadLen);

        var frame = new byte[frameLenVarInt + payloadLen];
        var offset = 0;
        offset += VarInt.Write(frame.AsSpan(offset), payloadLen);
        offset += VarInt.Write(frame.AsSpan(offset), Protocol340Ids.S2C_LoginSuccess);
        offset += VarInt.Write(frame.AsSpan(offset), uuidBytes.Length);
        uuidBytes.CopyTo(frame.AsSpan(offset));
        offset += uuidBytes.Length;
        offset += VarInt.Write(frame.AsSpan(offset), nameBytes.Length);
        nameBytes.CopyTo(frame.AsSpan(offset));
        return frame;
    }

    private void SendLoginDisconnectAndClose(ConnectionContext ctx, string reason)
    {
        if (ctx.Closed)
            return;

        var frame = BuildLoginDisconnectFrame(reason);
        var item = new SendWorkItem
        {
            Segment1 = new ArraySegment<byte>(frame, 0, frame.Length),
            Segment2 = default,
            TotalLength = frame.Length
        };

        ctx.CloseAfterLoginDisconnect = true;
        EnqueueSend(ctx, ctx.ClientSend, item, ctx.Client, ctx.ClientSendSaea, "client-send");
    }

    private static byte[] BuildLoginDisconnectFrame(string reason)
    {
        var reasonJson = $"{{\"text\":\"{EscapeJson(reason)}\"}}";
        var reasonBytes = Encoding.UTF8.GetBytes(reasonJson);
        Span<byte> varIntBuf = stackalloc byte[5];

        var reasonLenVarInt = VarInt.Write(varIntBuf, reasonBytes.Length);
        var payloadLen = 1 + reasonLenVarInt + reasonBytes.Length;
        var frameLenVarInt = VarInt.Write(varIntBuf, payloadLen);

        var frame = new byte[frameLenVarInt + payloadLen];
        var offset = 0;
        offset += VarInt.Write(frame.AsSpan(offset), payloadLen);
        offset += VarInt.Write(frame.AsSpan(offset), Protocol340Ids.S2C_LoginDisconnect);
        offset += VarInt.Write(frame.AsSpan(offset), reasonBytes.Length);
        reasonBytes.CopyTo(frame.AsSpan(offset));
        return frame;
    }

    private static string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private void LogParsedClientPacket(ConnectionContext ctx, ConnState stateBefore, int packetId, int frameLen, int payloadLen,
        object? parsed, bool forwardToBackend)
    {
        var result = new
        {
            Direction = "client->gateway",
            ContextId = ctx.Id,
            State = stateBefore.ToString(),
            PacketId = $"0x{packetId:X2}",
            FrameLength = frameLen,
            PayloadLength = payloadLen,
            ForwardToBackend = forwardToBackend,
            Parsed = parsed
        };

        Logger.Information("Parsed packet {@Packet}", result);
    }

    private void EnqueueBackendZeroCopy(ConnectionContext ctx, ArraySegment<byte> seg1, ArraySegment<byte> seg2, int totalLen)
    {
        if (!ctx.HasBackend)
        {
            Logger.Warning("Drop forward packet because backend is not attached, ctxId={ContextId}", ctx.Id);
            CloseContext(ctx, false, "backend-not-attached");
            return;
        }

        var item = new SendWorkItem
        {
            Segment1 = seg1,
            Segment2 = seg2,
            TotalLength = totalLen,
            FromClientInboundRing = true
        };

        EnqueueSend(ctx, ctx.BackendSend, item, GetBackendSocketOrThrow(ctx), GetBackendSendSaeaOrThrow(ctx), "backend-send");
    }

    private void EnqueueClientSend(ConnectionContext ctx, byte[] rent, int len)
    {
        var item = new SendWorkItem
        {
            Segment1 = new ArraySegment<byte>(rent, 0, len),
            Segment2 = default,
            TotalLength = len,
            Rented = rent
        };

        EnqueueSend(ctx, ctx.ClientSend, item, ctx.Client, ctx.ClientSendSaea, "client-send");
    }

    private void EnqueueSend(ConnectionContext ctx, SendChannel channel, SendWorkItem item, Socket socket, SocketAsyncEventArgs saea,
        string channelName)
    {
        bool start;
        lock (channel.Sync)
        {
            channel.Queue.Enqueue(item);
            start = !channel.Sending;
            if (start)
                channel.Sending = true;
        }

        if (start)
            TrySendNext(ctx, channel, socket, saea, channelName);
    }

    private void TrySendNext(ConnectionContext ctx, SendChannel channel, Socket socket, SocketAsyncEventArgs saea, string channelName)
    {
        while (true)
        {
            if (ctx.Closed)
                return;

            SendWorkItem? item;
            ArraySegment<byte> seg;

            lock (channel.Sync)
            {
                if (channel.Current == null)
                {
                    if (channel.Queue.Count == 0)
                    {
                        channel.Sending = false;
                        return;
                    }

                    channel.Current = channel.Queue.Dequeue();
                }

                item = channel.Current;
                seg = GetCurrentSegment(item);
            }

            if (seg.Count == 0)
            {
                CompleteCurrentSendItem(ctx, channel);
                continue;
            }

            saea.BufferList = null;
            saea.SetBuffer(seg.Array!, seg.Offset, seg.Count);
            if (!socket.SendAsync(saea))
            {
                HandleSendCompleted(saea, ctx, _ => channel, _ => socket, channelName);
                return;
            }

            return;
        }
    }

    private void HandleSendCompleted(SocketAsyncEventArgs e, ConnectionContext ctx, Func<ConnectionContext, SendChannel> channelSelector,
        Func<ConnectionContext, Socket> socketSelector, string channelName)
    {
        if (ctx.Closed)
            return;

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            Logger.Debug("Send failed, ctxId={ContextId}, channel={Channel}, socketError={SocketError}, bytes={Bytes}",
                ctx.Id, channelName, e.SocketError, e.BytesTransferred);
            CloseContext(ctx, false, "send-failed");
            return;
        }

        ctx.LastActivityTicks = Stopwatch.GetTimestamp();
        var channel = channelSelector(ctx);
        bool finished;

        lock (channel.Sync)
        {
            var item = channel.Current;
            if (item == null)
            {
                Logger.Warning("Send channel invariant broken, ctxId={ContextId}, channel={Channel}", ctx.Id, channelName);
                CloseContext(ctx, false, "send-channel-invariant");
                return;
            }

            finished = AdvanceSendItem(item, e.BytesTransferred);
        }

        if (finished)
            CompleteCurrentSendItem(ctx, channel);

        if (finished && ReferenceEquals(channel, ctx.ClientSend) && ctx.CloseAfterStatusPong)
        {
            CloseContext(ctx, false, "status-pong-sent");
            return;
        }
        if (finished && ReferenceEquals(channel, ctx.ClientSend) && ctx.CloseAfterLoginDisconnect)
        {
            CloseContext(ctx, false, "login-disconnect-sent");
            return;
        }

        if (!ctx.Closed)
            TrySendNext(ctx, channel, socketSelector(ctx), e, channelName);
    }

    private static ArraySegment<byte> GetCurrentSegment(SendWorkItem item)
    {
        var seg = item.SegmentIndex == 0 ? item.Segment1 : item.Segment2;
        if (seg.Count == 0)
            return default;

        return new ArraySegment<byte>(seg.Array!, seg.Offset + item.SegmentOffset, seg.Count - item.SegmentOffset);
    }

    private static bool AdvanceSendItem(SendWorkItem item, int sent)
    {
        while (sent > 0)
        {
            var seg = item.SegmentIndex == 0 ? item.Segment1 : item.Segment2;
            var remain = seg.Count - item.SegmentOffset;
            if (remain <= 0)
            {
                item.SegmentIndex++;
                item.SegmentOffset = 0;
                if (item.SegmentIndex > 1)
                    return true;
                continue;
            }

            if (sent < remain)
            {
                item.SegmentOffset += sent;
                return false;
            }

            sent -= remain;
            item.SegmentIndex++;
            item.SegmentOffset = 0;
            if (item.SegmentIndex > 1)
                return true;
        }

        return item.SegmentIndex > 1;
    }

    private static void CompleteCurrentSendItem(ConnectionContext ctx, SendChannel channel)
    {
        SendWorkItem? item;
        lock (channel.Sync)
        {
            item = channel.Current;
            channel.Current = null;
        }

        if (item == null)
            return;

        if (item.FromClientInboundRing)
        {
            lock (ctx.ParserSync)
            {
                ctx.ClientInbound.Skip(item.TotalLength);
                ctx.ParsedClientBytes -= item.TotalLength;
                if (ctx.ParsedClientBytes < 0)
                    ctx.ParsedClientBytes = 0;
            }
        }

        if (item.Rented != null)
            ArrayPool<byte>.Shared.Return(item.Rented);
    }

    private bool TryAcquireLoginSlot(ConnectionContext ctx)
    {
        if (ctx.LoginSlotHeld)
            return true;

        while (true)
        {
            var cur = Volatile.Read(ref _loginInProgress);
            if (cur >= LoginInProgressLimit)
                return false;

            if (Interlocked.CompareExchange(ref _loginInProgress, cur + 1, cur) == cur)
            {
                ctx.LoginSlotHeld = true;
                return true;
            }
        }
    }

    private void ReleaseLoginSlot(ConnectionContext ctx)
    {
        if (!ctx.LoginSlotHeld)
            return;

        ctx.LoginSlotHeld = false;
        Interlocked.Decrement(ref _loginInProgress);
    }

    private void RegisterInvalidPacketAndClose(ConnectionContext ctx)
    {
        var count = Interlocked.Increment(ref ctx.InvalidPacketCount);
        Logger.Warning("Invalid packet detected, ctxId={ContextId}, count={Count}, threshold={Threshold}",
            ctx.Id, count, InvalidPacketBanThreshold);
        if (count >= InvalidPacketBanThreshold)
            CloseContext(ctx, true, "invalid-packet-threshold");
        else
            CloseContext(ctx, false, "invalid-packet");
    }

    private void CheckTimeouts(object? _)
    {
        var now = Stopwatch.GetTimestamp();
        foreach (var entry in _contexts)
        {
            var ctx = entry.Value;
            if (ctx.Closed)
                continue;

            TimeSpan timeout;
            if (ctx.State == ConnState.Handshake)
                timeout = HandshakeTimeout;
            else if (ctx.State == ConnState.Login)
                timeout = LoginTimeout;
            else
                continue;

            var delta = (now - Volatile.Read(ref ctx.LastActivityTicks)) / _ticksPerSec;
            if (delta > timeout.TotalSeconds)
            {
                Logger.Warning("Connection timeout, ctxId={ContextId}, state={State}, elapsedSeconds={ElapsedSeconds:F2}",
                    ctx.Id, ctx.State, delta);
                CloseContext(ctx, false, "state-timeout");
            }
        }
    }

    private void CloseContext(ConnectionContext ctx, bool tempBan, string reason)
    {
        if (!ctx.TryMarkClosed())
            return;

        Logger.Information("Closing connection, ctxId={ContextId}, ip={Ip}, state={State}, tempBan={TempBan}, reason={Reason}",
            ctx.Id, FormatIpv4(ctx.IpV4), ctx.State, tempBan, reason);

        if (tempBan)
        {
            _blacklist.TempBan(ctx.IpV4, TempBanDuration);
            Logger.Warning("Temporary ban applied, ip={Ip}, duration={DurationSeconds}s",
                FormatIpv4(ctx.IpV4), TempBanDuration.TotalSeconds);
        }

        ReleaseLoginSlot(ctx);

        SafeClose(ctx.Client);
        SafeClose(ctx.Backend);

        ClearSendChannel(ctx, ctx.BackendSend);
        ClearSendChannel(ctx, ctx.ClientSend);

        ReturnSaea(ctx.ClientRecvSaea);
        ReturnSaea(ctx.BackendRecvSaea);
        ctx.BackendRecvSaea = null;
        ReturnSaea(ctx.BackendSendSaea);
        ctx.BackendSendSaea = null;
        ReturnSaea(ctx.ClientSendSaea);
        ctx.Backend = null;

        ctx.ClientInbound.Dispose();

        _contexts.TryRemove(ctx.Id, out _);
    }

    private static void SafeClose(Socket? socket)
    {
        if (socket == null)
            return;

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

    private static void ClearSendChannel(ConnectionContext ctx, SendChannel channel)
    {
        lock (channel.Sync)
        {
            if (channel.Current?.Rented != null)
                ArrayPool<byte>.Shared.Return(channel.Current.Rented);

            while (channel.Queue.Count > 0)
            {
                var item = channel.Queue.Dequeue();
                if (item.Rented != null)
                    ArrayPool<byte>.Shared.Return(item.Rented);
            }

            channel.Current = null;
            channel.Sending = false;
        }
    }

    private void ReturnSaea(SocketAsyncEventArgs? saea)
    {
        if (saea == null)
            return;

        ReturnIoToken(saea);
        _ioPool.Return(saea);
    }

    private static Socket GetBackendSocketOrThrow(ConnectionContext ctx)
    {
        if (ctx.Backend == null)
            throw new InvalidOperationException("Backend socket is not attached.");

        return ctx.Backend;
    }

    private static SocketAsyncEventArgs GetBackendSendSaeaOrThrow(ConnectionContext ctx)
    {
        if (ctx.BackendSendSaea == null)
            throw new InvalidOperationException("Backend send SAEA is not attached.");

        return ctx.BackendSendSaea;
    }

    private static string FormatIpv4(uint ip)
    {
        return $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
    }

    private void SendStatusResponse(ConnectionContext ctx)
    {
        var rent = ArrayPool<byte>.Shared.Rent(StatusResponseFrame.Length);
        Buffer.BlockCopy(StatusResponseFrame, 0, rent, 0, StatusResponseFrame.Length);
        var item = new SendWorkItem
        {
            Segment1 = new ArraySegment<byte>(rent, 0, StatusResponseFrame.Length),
            Segment2 = default,
            TotalLength = StatusResponseFrame.Length,
            Rented = rent
        };

        EnqueueSend(ctx, ctx.ClientSend, item, ctx.Client, ctx.ClientSendSaea, "client-send");
    }

    private void SendStatusPongAndClose(ConnectionContext ctx, long payload)
    {
        var frame = BuildStatusPongFrame(payload);
        var item = new SendWorkItem
        {
            Segment1 = new ArraySegment<byte>(frame, 0, frame.Length),
            Segment2 = default,
            TotalLength = frame.Length,
            Rented = frame
        };

        ctx.CloseAfterStatusPong = true;
        EnqueueSend(ctx, ctx.ClientSend, item, ctx.Client, ctx.ClientSendSaea, "client-send");
    }

    private static byte[] BuildStatusResponseFrame()
    {
        const string fallbackJson =
            "{\"version\":{\"name\":\"1.12.2\",\"protocol\":340},\"players\":{\"max\":100,\"online\":0,\"sample\":[]},\"description\":{\"text\":\"MC GateWay 1.12.2\"}}";

        var json = LoadStatusResponseJsonOrFallback(fallbackJson);

        var jsonBytes = Encoding.UTF8.GetBytes(json);
        Span<byte> varIntBuf = stackalloc byte[5];

        var jsonLenVarInt = VarInt.Write(varIntBuf, jsonBytes.Length);
        var payloadLen = 1 + jsonLenVarInt + jsonBytes.Length; // packetId + string len + string bytes
        var frameLenVarInt = VarInt.Write(varIntBuf, payloadLen);

        var frame = new byte[frameLenVarInt + payloadLen];
        var offset = 0;

        offset += VarInt.Write(frame.AsSpan(offset), payloadLen);
        frame[offset++] = (byte)Protocol340Ids.S2C_StatusResponse;
        offset += VarInt.Write(frame.AsSpan(offset), jsonBytes.Length);
        jsonBytes.CopyTo(frame.AsSpan(offset));

        return frame;
    }

    private static string LoadStatusResponseJsonOrFallback(string fallbackJson)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, StatusResponseFileName);
        try
        {
            if (!File.Exists(filePath))
            {
                Logger.Warning("Status response file not found, using fallback JSON. path={Path}", filePath);
                return fallbackJson;
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                Logger.Warning("Status response file is empty, using fallback JSON. path={Path}", filePath);
                return fallbackJson;
            }

            return json;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Read status response file failed, using fallback JSON. path={Path}", filePath);
            return fallbackJson;
        }
    }

    private static byte[] BuildStatusPongFrame(long payload)
    {
        const int pongPayloadLen = 1 + 8; // packetId + long
        Span<byte> varIntBuf = stackalloc byte[5];
        var frameLenVarInt = VarInt.Write(varIntBuf, pongPayloadLen);
        var frame = ArrayPool<byte>.Shared.Rent(frameLenVarInt + pongPayloadLen);

        var offset = 0;
        offset += VarInt.Write(frame.AsSpan(offset), pongPayloadLen);
        frame[offset++] = (byte)Protocol340Ids.S2C_StatusPong;

        var value = unchecked((ulong)payload);
        for (var i = 7; i >= 0; i--)
        {
            frame[offset + i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        return frame;
    }
}


