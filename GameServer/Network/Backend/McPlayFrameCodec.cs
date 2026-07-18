using System.Net.Sockets;
using Common.MC;

namespace GameServer.Network.Backend;

public static class McPlayFrameCodec
{
    public const int MaxFramePayload = 2 * 1024 * 1024;

    /// <summary>Read a VarInt from the span. Delegates to Common VarIntCodec.</summary>
    public static bool TryReadVarInt(ReadOnlySpan<byte> source, ref int offset, out int value)
        => VarIntCodec.TryRead(source, ref offset, out value);

    public static async ValueTask<byte[]?> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[5];
        var lengthSize = 0;

        while (lengthSize < 5)
        {
            var read = await stream.ReadAsync(lengthBytes.AsMemory(lengthSize, 1), cancellationToken);
            if (read == 0)
                return null;

            if ((lengthBytes[lengthSize] & 0b1000_0000) == 0)
            {
                lengthSize++;
                break;
            }

            lengthSize++;
        }

        var offset = 0;
        if (!TryReadVarInt(lengthBytes.AsSpan(0, lengthSize), ref offset, out var payloadLen))
            throw new InvalidOperationException("Invalid frame length varint.");
        if (payloadLen < 0 || payloadLen > MaxFramePayload)
            throw new InvalidOperationException($"Invalid frame payload length: {payloadLen}");

        var frame = new byte[lengthSize + payloadLen];
        Array.Copy(lengthBytes, 0, frame, 0, lengthSize);

        var copied = 0;
        while (copied < payloadLen)
        {
            var read = await stream.ReadAsync(frame.AsMemory(lengthSize + copied, payloadLen - copied), cancellationToken);
            if (read == 0)
                return null;

            copied += read;
        }

        return frame;
    }

    public static bool TryGetPacketId(ReadOnlySpan<byte> frame, out int packetId)
    {
        packetId = 0;

        var offset = 0;
        if (!TryReadVarInt(frame, ref offset, out var payloadLen))
            return false;
        if (payloadLen < 0 || payloadLen > MaxFramePayload)
            return false;
        if (offset + payloadLen != frame.Length)
            return false;

        return TryReadVarInt(frame, ref offset, out packetId);
    }
}
