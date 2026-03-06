// GatewayServer.cs

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace GateWay;

public sealed class GateWayServer
{
    private const int AcceptPoolInitial = 256;
    private const int AcceptPoolMax = 2048;
    private const int IoPoolInitial = 4096;
    private const int IoPoolMax = 50000;
    private const int IoBufferSize = 32 * 1024;
    private const int InvalidPacketBanThreshold = 3;
    private const int LoginInProgressLimit = 2000;
    private static readonly TimeSpan TempBanDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(5);

    private readonly IPEndPoint _backendEp;
    private readonly Blacklist _blacklist;
    private readonly RateLimiter _limiter;
    private readonly IPEndPoint _listenEp;
    private readonly SaeaPool _acceptPool;
    private readonly ConcurrentDictionary<int, ConnectionContext> _contexts = new();
    private readonly SaeaPool _ioPool;
    private readonly double _ticksPerSec = Stopwatch.Frequency;

    private Socket _listen = null!;
    private int _loginInProgress;
    private int _nextContextId;
    private Timer? _timeoutTimer;

    public GateWayServer(IPEndPoint listenEp, IPEndPoint backendEp, Blacklist blacklist, RateLimiter limiter)
    {
        _listenEp = listenEp;
        _backendEp = backendEp;
        _blacklist = blacklist;
        _limiter = limiter;

        _acceptPool = new SaeaPool(AcceptPoolInitial, AcceptPoolMax, 0, OnIoCompleted);
        _ioPool = new SaeaPool(IoPoolInitial, IoPoolMax, IoBufferSize, OnIoCompleted);
    }

    public void Start()
    {
        _listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listen.NoDelay = true;
        _listen.Bind(_listenEp);
        _listen.Listen(8192);

        _timeoutTimer = new Timer(CheckTimeouts, null, 1000, 1000);
        StartAccept();
        Console.WriteLine($"Gateway listening on {_listenEp}, backend={_backendEp}");
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
                HandleSendCompleted(e, token.Context!, ctx => ctx.BackendSend, ctx => ctx.Backend);
                break;
            case IoOperation.ClientSend:
                HandleSendCompleted(e, token.Context!, ctx => ctx.ClientSend, ctx => ctx.Client);
                break;
        }
    }

    private void StartAccept()
    {
        if (!_acceptPool.TryRent(out var saea))
        {
            ThreadPool.QueueUserWorkItem(_ => StartAccept());
            return;
        }

        saea.AcceptSocket = null;
        saea.UserToken = new IoToken { Operation = IoOperation.Accept, Context = null };
        if (!_listen.AcceptAsync(saea))
            HandleAccept(saea);
    }

    private void HandleAccept(SocketAsyncEventArgs e)
    {
        var client = e.AcceptSocket;
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
                client.Close();
                return;
            }

            var ip = IpUtils.ToUInt32(remote.Address);
            if (_blacklist.IsBlocked(ip))
            {
                client.Close();
                return;
            }

            if (!_limiter.TryAcceptConnection(ip))
            {
                client.Close();
                return;
            }

            var backend = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            backend.NoDelay = true;
            backend.Connect(_backendEp);

            var ctx = new ConnectionContext
            {
                Client = client,
                Backend = backend,
                IpV4 = ip,
                State = ConnState.Handshake,
                LastActivityTicks = Stopwatch.GetTimestamp()
            };

            if (!TryRentConnectionSaeas(ctx))
            {
                CloseContext(ctx, false);
                return;
            }

            var id = Interlocked.Increment(ref _nextContextId);
            ctx.Id = id;
            _contexts[id] = ctx;

            StartReceive(ctx.Client, ctx.ClientRecvSaea);
            StartReceive(ctx.Backend, ctx.BackendRecvSaea);
        }
        catch
        {
            SafeClose(client);
        }
    }

    private bool TryRentConnectionSaeas(ConnectionContext ctx)
    {
        if (!TryRentIo(ctx, IoOperation.ClientRecv, out var cRecv))
            return false;
        if (!TryRentIo(ctx, IoOperation.BackendRecv, out var bRecv))
        {
            _ioPool.Return(cRecv);
            return false;
        }

        if (!TryRentIo(ctx, IoOperation.BackendSend, out var bSend))
        {
            _ioPool.Return(cRecv);
            _ioPool.Return(bRecv);
            return false;
        }

        if (!TryRentIo(ctx, IoOperation.ClientSend, out var cSend))
        {
            _ioPool.Return(cRecv);
            _ioPool.Return(bRecv);
            _ioPool.Return(bSend);
            return false;
        }

        ctx.ClientRecvSaea = cRecv;
        ctx.BackendRecvSaea = bRecv;
        ctx.BackendSendSaea = bSend;
        ctx.ClientSendSaea = cSend;
        return true;
    }

    private bool TryRentIo(ConnectionContext ctx, IoOperation op, out SocketAsyncEventArgs saea)
    {
        if (!_ioPool.TryRent(out saea!))
            return false;

        saea.UserToken = new IoToken { Operation = op, Context = ctx };
        return true;
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
            CloseContext(ctx, false);
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
        catch
        {
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

            try
            {
                if (!McFrameParser.TryGetPacketId(frameSpan, out var pid, out _))
                {
                    RegisterInvalidPacketAndClose(ctx);
                    return;
                }

                if (!StateMachine.IsAllowed(ctx.State, pid))
                {
                    RegisterInvalidPacketAndClose(ctx);
                    return;
                }

                if (ctx.State == ConnState.Handshake && pid == Protocol340Ids.C2S_Handshake)
                {
                    if (!McFrameParser.TryParseHandshakeNextState(frameSpan, out var next))
                    {
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    if (next == 1)
                    {
                        ctx.State = ConnState.Status;
                    }
                    else
                    {
                        if (!TryAcquireLoginSlot(ctx))
                        {
                            CloseContext(ctx, false);
                            return;
                        }

                        ctx.State = ConnState.Login;
                    }
                }
                else if (ctx.State == ConnState.Login && pid == Protocol340Ids.C2S_LoginStart)
                {
                    if (!_limiter.TryAcceptLogin(ctx.IpV4))
                    {
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }

                    if (!McFrameParser.TryValidateLoginStart(frameSpan))
                    {
                        RegisterInvalidPacketAndClose(ctx);
                        return;
                    }
                }
            }
            finally
            {
                if (parseRent != null)
                    ArrayPool<byte>.Shared.Return(parseRent);
            }

            ctx.ParsedClientBytes += frameLen;
            EnqueueBackendZeroCopy(ctx, seg1, seg2, frameLen);
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

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            CloseContext(ctx, false);
            return;
        }

        ctx.LastActivityTicks = Stopwatch.GetTimestamp();
        TryPromoteLoginToPlay(ctx, e.Buffer.AsSpan(e.Offset, e.BytesTransferred));

        var rent = ArrayPool<byte>.Shared.Rent(e.BytesTransferred);
        Buffer.BlockCopy(e.Buffer!, e.Offset, rent, 0, e.BytesTransferred);

        EnqueueClientSend(ctx, rent, e.BytesTransferred);

        if (!ctx.Closed)
            StartReceive(ctx.Backend, e);
    }

    private void TryPromoteLoginToPlay(ConnectionContext ctx, ReadOnlySpan<byte> data)
    {
        if (ctx.State != ConnState.Login)
            return;

        var off = 0;
        if (!VarInt.TryRead(data, ref off, out _))
            return;

        if (!VarInt.TryRead(data, ref off, out var packetId))
            return;

        if (packetId != Protocol340Ids.S2C_LoginSuccess)
            return;

        ctx.State = ConnState.Play;
        ReleaseLoginSlot(ctx);
    }

    private void EnqueueBackendZeroCopy(ConnectionContext ctx, ArraySegment<byte> seg1, ArraySegment<byte> seg2, int totalLen)
    {
        var item = new SendWorkItem
        {
            Segment1 = seg1,
            Segment2 = seg2,
            TotalLength = totalLen,
            FromClientInboundRing = true
        };

        EnqueueSend(ctx, ctx.BackendSend, item, ctx.Backend, ctx.BackendSendSaea);
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

        EnqueueSend(ctx, ctx.ClientSend, item, ctx.Client, ctx.ClientSendSaea);
    }

    private void EnqueueSend(ConnectionContext ctx, SendChannel channel, SendWorkItem item, Socket socket, SocketAsyncEventArgs saea)
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
            TrySendNext(ctx, channel, socket, saea);
    }

    private void TrySendNext(ConnectionContext ctx, SendChannel channel, Socket socket, SocketAsyncEventArgs saea)
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
                HandleSendCompleted(saea, ctx, _ => channel, _ => socket);
                return;
            }

            return;
        }
    }

    private void HandleSendCompleted(SocketAsyncEventArgs e, ConnectionContext ctx, Func<ConnectionContext, SendChannel> channelSelector,
        Func<ConnectionContext, Socket> socketSelector)
    {
        if (ctx.Closed)
            return;

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            CloseContext(ctx, false);
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
                CloseContext(ctx, false);
                return;
            }

            finished = AdvanceSendItem(item, e.BytesTransferred);
        }

        if (finished)
            CompleteCurrentSendItem(ctx, channel);

        if (!ctx.Closed)
            TrySendNext(ctx, channel, socketSelector(ctx), e);
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

    private void CompleteCurrentSendItem(ConnectionContext ctx, SendChannel channel)
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
        if (count >= InvalidPacketBanThreshold)
            CloseContext(ctx, true);
        else
            CloseContext(ctx, false);
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
                CloseContext(ctx, false);
        }
    }

    private void CloseContext(ConnectionContext ctx, bool tempBan)
    {
        if (!ctx.TryMarkClosed())
            return;

        if (tempBan)
            _blacklist.TempBan(ctx.IpV4, TempBanDuration);

        ReleaseLoginSlot(ctx);

        SafeClose(ctx.Client);
        SafeClose(ctx.Backend);

        ClearSendChannel(ctx, ctx.BackendSend);
        ClearSendChannel(ctx, ctx.ClientSend);

        ReturnSaea(ctx.ClientRecvSaea);
        ReturnSaea(ctx.BackendRecvSaea);
        ReturnSaea(ctx.BackendSendSaea);
        ReturnSaea(ctx.ClientSendSaea);

        ctx.ClientInbound.Dispose();

        _contexts.TryRemove(ctx.Id, out _);
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

    private void ReturnSaea(SocketAsyncEventArgs saea)
    {
        if (saea != null)
            _ioPool.Return(saea);
    }
}
