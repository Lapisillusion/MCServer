using System.Buffers;

namespace GameServer.Core.Session;

/// <summary>
/// An ArrayPool-backed network write. Ownership transfers to SessionContext after a successful enqueue.
/// </summary>
public sealed class OutboundBatch : IDisposable
{
    private byte[]? _buffer;

    private OutboundBatch(byte[] buffer)
    {
        _buffer = buffer;
    }

    public int Length { get; private set; }
    public bool IsDisposed => Volatile.Read(ref _buffer) == null;
    public Memory<byte> Memory => GetBuffer().AsMemory(0, Length);
    public Span<byte> WritableSpan => GetBuffer();

    public static OutboundBatch Rent(int minimumLength)
    {
        if (minimumLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        return new OutboundBatch(ArrayPool<byte>.Shared.Rent(minimumLength));
    }

    public void SetLength(int length)
    {
        var buffer = GetBuffer();
        if ((uint)length > (uint)buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer == null)
            return;

        Length = 0;
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private byte[] GetBuffer()
        => Volatile.Read(ref _buffer) ?? throw new ObjectDisposedException(nameof(OutboundBatch));
}
