// SaeaPool.cs

using System.Collections.Concurrent;
using System.Threading;
using System.Net.Sockets;

namespace Common;

public sealed class SaeaPool
{
    private readonly ConcurrentBag<SocketAsyncEventArgs> _bag = new();
    private readonly int _bufferSize;
    private readonly EventHandler<SocketAsyncEventArgs> _completedHandler;
    private readonly int _maxCount;
    private int _created;

    public SaeaPool(int initial, int maxCount, int bufferSize, EventHandler<SocketAsyncEventArgs> completedHandler)
    {
        if (initial < 0) throw new ArgumentOutOfRangeException(nameof(initial));
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        if (initial > maxCount) throw new ArgumentOutOfRangeException(nameof(initial));

        _bufferSize = bufferSize;
        _maxCount = maxCount;
        _completedHandler = completedHandler;

        for (var i = 0; i < initial; i++)
        {
            _bag.Add(Create());
            _created++;
        }
    }

    private SocketAsyncEventArgs Create()
    {
        var saea = new SocketAsyncEventArgs();
        saea.Completed += _completedHandler;
        if (_bufferSize > 0)
            saea.SetBuffer(new byte[_bufferSize], 0, _bufferSize);
        return saea;
    }

    public bool TryRent(out SocketAsyncEventArgs saea)
    {
        if (_bag.TryTake(out saea!))
            return true;

        while (true)
        {
            var created = Volatile.Read(ref _created);
            if (created >= _maxCount)
            {
                saea = null!;
                return false;
            }

            if (Interlocked.CompareExchange(ref _created, created + 1, created) == created)
            {
                saea = Create();
                return true;
            }
        }
    }

    public void Return(SocketAsyncEventArgs saea)
    {
        saea.AcceptSocket = null;
        saea.UserToken = null;
        saea.BufferList = null;
        saea.RemoteEndPoint = null;
        if (_bufferSize > 0 && saea.Buffer != null)
            saea.SetBuffer(0, _bufferSize);
        _bag.Add(saea);
    }
}
