// ObjectPool.cs

using System.Collections.Concurrent;
using System.Threading;

namespace Common;

public interface IPooledObject
{
    // Called when returning to pool.
    void OnReturnToPool();

    // Called before the object is discarded permanently.
    void OnPoolDestroy();
}

public readonly struct PoolLease<T> : IDisposable where T : class
{
    private readonly ObjectPool<T>? _pool;
    public T Item { get; }

    internal PoolLease(ObjectPool<T> pool, T item)
    {
        _pool = pool;
        Item = item;
    }

    public void Dispose()
    {
        _pool?.Return(Item);
    }
}

public sealed class ObjectPool<T> : IDisposable where T : class
{
    private readonly ConcurrentBag<T> _bag = new();
    private readonly Func<T> _factory;
    private readonly int _maxCount;
    private readonly Action<T>? _onReturn;
    private readonly Action<T>? _onDestroy;
    private int _created;
    private int _disposed;

    public ObjectPool(int initial, int maxCount, Func<T> factory, Action<T>? onReturn = null, Action<T>? onDestroy = null)
    {
        if (initial < 0) throw new ArgumentOutOfRangeException(nameof(initial));
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        if (initial > maxCount) throw new ArgumentOutOfRangeException(nameof(initial));

        _maxCount = maxCount;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _onReturn = onReturn;
        _onDestroy = onDestroy;

        for (var i = 0; i < initial; i++)
        {
            _bag.Add(_factory());
            _created++;
        }
    }

    public bool TryRent(out T item)
    {
        ThrowIfDisposed();

        if (_bag.TryTake(out item!))
            return true;

        while (true)
        {
            var created = Volatile.Read(ref _created);
            if (created >= _maxCount)
            {
                item = null!;
                return false;
            }

            if (Interlocked.CompareExchange(ref _created, created + 1, created) == created)
            {
                item = _factory();
                return true;
            }
        }
    }

    public T Rent()
    {
        if (!TryRent(out var item))
            throw new InvalidOperationException("Object pool exhausted.");
        return item;
    }

    public PoolLease<T> RentLease()
    {
        return new PoolLease<T>(this, Rent());
    }

    public void Return(T item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        if (Volatile.Read(ref _disposed) != 0)
        {
            DestroyItem(item);
            return;
        }

        _onReturn?.Invoke(item);
        _bag.Add(item);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        while (_bag.TryTake(out var item))
            DestroyItem(item);
    }

    private void DestroyItem(T item)
    {
        try
        {
            _onDestroy?.Invoke(item);
        }
        finally
        {
            Interlocked.Decrement(ref _created);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(ObjectPool<T>));
    }
}

public sealed class ListPool<T> : IDisposable
{
    private readonly ObjectPool<List<T>> _inner;

    public ListPool(int initial, int maxCount, int initialListCapacity = 0)
    {
        _inner = new ObjectPool<List<T>>(
            initial,
            maxCount,
            () => initialListCapacity > 0 ? new List<T>(initialListCapacity) : new List<T>(),
            list => list.Clear(),
            list => list.Clear());
    }

    public bool TryRent(out List<T> list) => _inner.TryRent(out list!);
    public List<T> Rent() => _inner.Rent();
    public PoolLease<List<T>> RentLease() => _inner.RentLease();
    public void Return(List<T> list) => _inner.Return(list);
    public void Dispose() => _inner.Dispose();
}

public sealed class DictionaryPool<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly ObjectPool<Dictionary<TKey, TValue>> _inner;

    public DictionaryPool(int initial, int maxCount, int initialCapacity = 0, IEqualityComparer<TKey>? comparer = null)
    {
        _inner = new ObjectPool<Dictionary<TKey, TValue>>(
            initial,
            maxCount,
            () => initialCapacity > 0
                ? new Dictionary<TKey, TValue>(initialCapacity, comparer)
                : new Dictionary<TKey, TValue>(comparer),
            dict => dict.Clear(),
            dict => dict.Clear());
    }

    public bool TryRent(out Dictionary<TKey, TValue> dict) => _inner.TryRent(out dict!);
    public Dictionary<TKey, TValue> Rent() => _inner.Rent();
    public PoolLease<Dictionary<TKey, TValue>> RentLease() => _inner.RentLease();
    public void Return(Dictionary<TKey, TValue> dict) => _inner.Return(dict);
    public void Dispose() => _inner.Dispose();
}

public sealed class PooledObjectPool<T> : IDisposable where T : class, IPooledObject, new()
{
    private readonly ObjectPool<T> _inner;

    public PooledObjectPool(int initial, int maxCount)
    {
        _inner = new ObjectPool<T>(initial, maxCount, () => new T(), item => item.OnReturnToPool(),
            item => item.OnPoolDestroy());
    }

    public bool TryRent(out T item) => _inner.TryRent(out item!);
    public T Rent() => _inner.Rent();
    public PoolLease<T> RentLease() => _inner.RentLease();
    public void Return(T item) => _inner.Return(item);
    public void Dispose() => _inner.Dispose();
}
