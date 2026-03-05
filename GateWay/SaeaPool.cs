// SaeaPool.cs

using System.Collections.Concurrent;
using System.Net.Sockets;

namespace GateWay;

public sealed class SaeaPool
{
    private readonly ConcurrentBag<SocketAsyncEventArgs> _bag = new();
    private readonly int _bufferSize;

    public SaeaPool(int initial, int bufferSize)
    {
        _bufferSize = bufferSize;
        for (var i = 0; i < initial; i++)
            _bag.Add(Create());
    }

    private SocketAsyncEventArgs Create()
    {
        var saea = new SocketAsyncEventArgs();
        saea.SetBuffer(new byte[_bufferSize], 0, _bufferSize);
        return saea;
    }

    public SocketAsyncEventArgs Rent()
    {
        return _bag.TryTake(out var saea) ? saea : Create();
    }

    public void Return(SocketAsyncEventArgs saea)
    {
        saea.AcceptSocket = null;
        saea.UserToken = null;
        saea.RemoteEndPoint = null;
        saea.SocketError = SocketError.Success;
        saea.SetBuffer(0, _bufferSize);
        _bag.Add(saea);
    }
}