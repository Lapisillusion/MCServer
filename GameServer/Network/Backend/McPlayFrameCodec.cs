using System.Net.Sockets;

namespace GameServer.Network.Backend;

public static class McPlayFrameCodec
{
    public const int MaxFramePayload = 2 * 1024 * 1024;

    public static bool TryReadVarInt(ReadOnlySpan<byte> source, ref int offset, out int value)
    {
        value = 0;
        var numRead = 0;

        while (true)
        {
            if (offset >= source.Length)
            {
                value = 0;
                return false;
            }

            var current = source[offset++];
            var payload = current & 0b0111_1111;
            value |= payload << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                value = 0;
                return false;
            }

            if ((current & 0b1000_0000) == 0)
                return true;
        }
    }

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
