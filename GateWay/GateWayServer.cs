// GatewayServer.cs

using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace GateWay;

public sealed class GateWayServer
{
    private readonly IPEndPoint _backendEp;
    private readonly Blacklist _blacklist;
    private readonly RateLimiter _limiter;
    private readonly IPEndPoint _listenEp;

    private readonly SaeaPool _saeaPool;

    private Socket _listen = null!;

    public GateWayServer(IPEndPoint listenEp, IPEndPoint backendEp, Blacklist blacklist, RateLimiter limiter)
    {
        _listenEp = listenEp;
        _backendEp = backendEp;
        _blacklist = blacklist;
        _limiter = limiter;

        _saeaPool = new SaeaPool(4096, 32 * 1024);
    }

    public void Start()
    {
        _listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listen.NoDelay = true;
        _listen.Bind(_listenEp);
        _listen.Listen(8192);

        StartAccept();
        Console.WriteLine($"Gateway listening on {_listenEp}, backend={_backendEp}");
    }

    private void StartAccept()
    {
        var saea = _saeaPool.Rent();
        saea.Completed += AcceptCompleted;
        if (!_listen.AcceptAsync(saea))
            AcceptCompleted(_listen, saea);
    }

    private void AcceptCompleted(object? sender, SocketAsyncEventArgs e)
    {
        e.Completed -= AcceptCompleted;

        var client = e.AcceptSocket;
        _saeaPool.Return(e);

        // 立刻继续 accept
        StartAccept();

        if (client == null) return;

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

            // 1) 黑名单
            if (_blacklist.IsBlocked(ip))
            {
                client.Close();
                return;
            }

            // 2) 连接限流
            if (!_limiter.TryAcceptConnection(ip))
            {
                client.Close();
                return;
            }

            // 3) 连接后端
            var backend = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            backend.NoDelay = true;
            backend.Connect(_backendEp);

            // 4) 建上下文
            var ctx = new ConnectionContext
            {
                Client = client,
                Backend = backend,
                IpV4 = ip,
                State = ConnState.Handshake
            };

            // 后端->客户端：原样转发（不解析）
            StartRecvBackend(ctx);

            // 客户端->后端：解析、校验、转发
            StartRecvClient(ctx);
        }
        catch
        {
            try
            {
                client.Close();
            }
            catch
            {
            }
        }
    }

    private void StartRecvClient(ConnectionContext ctx)
    {
        var saea = _saeaPool.Rent();
        saea.UserToken = ctx;
        saea.Completed += ClientRecvCompleted;
        saea.AcceptSocket = ctx.Client;

        if (!ctx.Client.ReceiveAsync(saea))
            ClientRecvCompleted(ctx.Client, saea);
    }

    private void ClientRecvCompleted(object? sender, SocketAsyncEventArgs e)
    {
        e.Completed -= ClientRecvCompleted;

        var ctx = (ConnectionContext)e.UserToken!;
        if (ctx.Closed)
        {
            _saeaPool.Return(e);
            return;
        }

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            _saeaPool.Return(e);
            ctx.Close();
            return;
        }

        // 写入 ring buffer
        ctx.Inbound.Write(e.Buffer.AsSpan(e.Offset, e.BytesTransferred));

        // 尝试解析并转发尽可能多帧
        try
        {
            ProcessClientFrames(ctx);
        }
        catch
        {
            _saeaPool.Return(e);
            ctx.Close();
            return;
        }

        // 继续收
        e.Completed += ClientRecvCompleted;
        if (!ctx.Client.ReceiveAsync(e))
            ClientRecvCompleted(ctx.Client, e);
    }

    private void ProcessClientFrames(ConnectionContext ctx)
    {
        // 解析 VarInt length 需要 peek 一点数据。stackalloc 小缓冲。
        Span<byte> peek5 = stackalloc byte[5];

        while (true)
        {
            if (!McFrameParser.TryReadFrame(ctx.Inbound, peek5, out var frameLenTotal))
                return;

            // 把该帧拷出来（MVP：直接租借数组再转发；后续可以做“零拷贝 + 分段发送”）
            var frame = ArrayPool<byte>.Shared.Rent(frameLenTotal);
            try
            {
                ctx.Inbound.Peek(frame.AsSpan(0, frameLenTotal), frameLenTotal);

                // 读 packetId
                if (!McFrameParser.TryGetPacketId(frame.AsSpan(0, frameLenTotal), out var pid, out _))
                {
                    _blacklist.TempBan(ctx.IpV4, TimeSpan.FromMinutes(5));
                    ctx.Close();
                    return;
                }

                // 状态机校验
                if (!StateMachine.IsAllowed(ctx.State, pid))
                {
                    _blacklist.TempBan(ctx.IpV4, TimeSpan.FromMinutes(5));
                    ctx.Close();
                    return;
                }

                // Handshake：解析 nextState 切换
                if (ctx.State == ConnState.Handshake && pid == Protocol340Ids.C2S_Handshake)
                {
                    if (!McFrameParser.TryParseHandshakeNextState(frame.AsSpan(0, frameLenTotal), out var next))
                    {
                        _blacklist.TempBan(ctx.IpV4, TimeSpan.FromMinutes(5));
                        ctx.Close();
                        return;
                    }

                    ctx.State = next == 1 ? ConnState.Status : ConnState.Login;
                }
                else if (ctx.State == ConnState.Login && pid == Protocol340Ids.C2S_LoginStart)
                {
                    // 登录限流
                    if (!_limiter.TryAcceptLogin(ctx.IpV4))
                    {
                        _blacklist.TempBan(ctx.IpV4, TimeSpan.FromMinutes(5));
                        ctx.Close();
                        return;
                    }

                    // 可选：用户名长度/字符校验
                    if (!McFrameParser.TryValidateLoginStart(frame.AsSpan(0, frameLenTotal)))
                    {
                        _blacklist.TempBan(ctx.IpV4, TimeSpan.FromMinutes(5));
                        ctx.Close();
                        return;
                    }
                }

                // 通过：转发到后端（原始帧：length+payload）
                // 这里为了简化直接同步 Send；你也可以换成后端的 SAEA SendAsync + 发送队列
                ctx.Backend.Send(frame, 0, frameLenTotal, SocketFlags.None);

                // 消费 ring buffer
                ctx.Inbound.Skip(frameLenTotal);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame);
            }
        }
    }

    private void StartRecvBackend(ConnectionContext ctx)
    {
        var saea = _saeaPool.Rent();
        saea.UserToken = ctx;
        saea.Completed += BackendRecvCompleted;
        saea.AcceptSocket = ctx.Backend;

        if (!ctx.Backend.ReceiveAsync(saea))
            BackendRecvCompleted(ctx.Backend, saea);
    }

    private void BackendRecvCompleted(object? sender, SocketAsyncEventArgs e)
    {
        e.Completed -= BackendRecvCompleted;

        var ctx = (ConnectionContext)e.UserToken!;
        if (ctx.Closed)
        {
            _saeaPool.Return(e);
            return;
        }

        if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
        {
            _saeaPool.Return(e);
            ctx.Close();
            return;
        }

        // 后端->客户端：原样转发（不解析）
        try
        {
            ctx.Client.Send(e.Buffer!, e.Offset, e.BytesTransferred, SocketFlags.None);
        }
        catch
        {
            _saeaPool.Return(e);
            ctx.Close();
            return;
        }

        // 继续收
        e.Completed += BackendRecvCompleted;
        if (!ctx.Backend.ReceiveAsync(e))
            BackendRecvCompleted(ctx.Backend, e);
    }
}