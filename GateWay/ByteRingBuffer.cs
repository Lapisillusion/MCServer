// ByteRingBuffer.cs

using System.Buffers;

namespace GateWay;

public sealed class ByteRingBuffer
{
    private byte[] _buf;
    private int _r; // read index
    private int _w; // write index

    public ByteRingBuffer(int initialCapacity = 64 * 1024)
    {
        _buf = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    public int Count { get; private set; }

    public int Capacity => _buf.Length;

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buf);
        _buf = Array.Empty<byte>();
        _r = _w = Count = 0;
    }

    public void EnsureWriteCapacity(int need)
    {
        if (need <= Capacity - Count) return;

        var newCap = Capacity;
        while (newCap - Count < need) newCap *= 2;

        var nb = ArrayPool<byte>.Shared.Rent(newCap);
        // copy existing data linearly into nb[0.._count)
        if (Count > 0)
        {
            if (_r < _w)
            {
                Buffer.BlockCopy(_buf, _r, nb, 0, Count);
            }
            else
            {
                var right = Capacity - _r;
                Buffer.BlockCopy(_buf, _r, nb, 0, right);
                Buffer.BlockCopy(_buf, 0, nb, right, _w);
            }
        }

        ArrayPool<byte>.Shared.Return(_buf);
        _buf = nb;
        _r = 0;
        _w = Count;
    }

    public void Write(ReadOnlySpan<byte> src)
    {
        EnsureWriteCapacity(src.Length);

        var first = Math.Min(src.Length, Capacity - _w);
        src[..first].CopyTo(_buf.AsSpan(_w, first));
        _w = (_w + first) % Capacity;
        Count += first;

        var remain = src.Length - first;
        if (remain > 0)
        {
            src.Slice(first, remain).CopyTo(_buf.AsSpan(_w, remain));
            _w += remain;
            Count += remain;
        }
    }

    // Peek: copy up to len bytes into dst without consuming
    public int Peek(Span<byte> dst, int len)
    {
        len = Math.Min(len, Count);
        if (len == 0) return 0;

        var first = Math.Min(len, Capacity - _r);
        _buf.AsSpan(_r, first).CopyTo(dst[..first]);
        var remain = len - first;
        if (remain > 0)
            _buf.AsSpan(0, remain).CopyTo(dst.Slice(first, remain));

        return len;
    }

    public void Skip(int len)
    {
        len = Math.Min(len, Count);
        _r = (_r + len) % Capacity;
        Count -= len;
        if (Count == 0) _r = _w; // normalize
    }
}