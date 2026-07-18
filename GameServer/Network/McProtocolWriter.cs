using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Common.MC;

namespace GameServer.Network;

/// <summary>
/// Minecraft protocol encoding utilities (protocol 340 / 1.12.2).
/// All multi-byte integers are big-endian. Strings are VarInt-length-prefixed UTF-8.
/// VarInt encoding delegates to Common.MC.VarIntCodec.
/// </summary>
public static class McProtocolWriter
{
    /// <summary>Write a VarInt to the span. Returns the number of bytes written.</summary>
    public static int WriteVarInt(Span<byte> dst, int value)
        => VarIntCodec.Write(dst, value);

    /// <summary>Returns the number of bytes needed to encode a VarInt value.</summary>
    public static int GetVarIntLength(int value)
        => VarIntCodec.GetLength(value);

    public static int WriteString(Span<byte> dst, string value)
    {
        var strBytes = Encoding.UTF8.GetBytes(value);
        var written = WriteVarInt(dst, strBytes.Length);
        strBytes.CopyTo(dst[written..]);
        return written + strBytes.Length;
    }

    public static int GetStringLength(string value)
    {
        var strLen = Encoding.UTF8.GetByteCount(value);
        return GetVarIntLength(strLen) + strLen;
    }

    public static int WriteDouble(Span<byte> dst, double value)
    {
        BinaryPrimitives.WriteDoubleBigEndian(dst, value);
        return 8;
    }

    public static int WriteFloat(Span<byte> dst, float value)
    {
        BinaryPrimitives.WriteSingleBigEndian(dst, value);
        return 4;
    }

    public static int WriteInt32(Span<byte> dst, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(dst, value);
        return 4;
    }

    public static int WriteInt64(Span<byte> dst, long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(dst, value);
        return 8;
    }

    public static int WriteByte(Span<byte> dst, byte value)
    {
        dst[0] = value;
        return 1;
    }

    public static int WriteBool(Span<byte> dst, bool value)
    {
        dst[0] = value ? (byte)1 : (byte)0;
        return 1;
    }

    /// <summary>Encode block position as a 64-bit long (26-bit X, 12-bit Y, 26-bit Z).</summary>
    public static long EncodePosition(long x, long y, long z)
    {
        return ((x & 0x3FFFFFFL) << 38) | ((y & 0xFFFL) << 26) | (z & 0x3FFFFFFL);
    }

    /// <summary>
    /// Build a complete MC protocol frame: [VarInt frameLength] [VarInt packetId] [payload].
    /// The returned byte array contains the full frame ready to write to the stream.
    /// </summary>
    public static byte[] BuildMcFrame(int packetId, ReadOnlySpan<byte> payload)
    {
        var pidLen = GetVarIntLength(packetId);
        var totalPayloadLen = pidLen + payload.Length;
        var frameLenLen = GetVarIntLength(totalPayloadLen);
        var frame = new byte[frameLenLen + totalPayloadLen];

        var offset = 0;
        offset += WriteVarInt(frame.AsSpan(offset), totalPayloadLen);
        offset += WriteVarInt(frame.AsSpan(offset), packetId);
        payload.CopyTo(frame.AsSpan(offset));
        return frame;
    }

    /// <summary>
    /// Allocates a span large enough and writes the packet, returning the used portion.
    /// Useful as a scratch buffer when building packets incrementally.
    /// </summary>
    public static byte[] BuildMcFrame(int packetId, Action<Span<byte>> writePayload, int payloadLength)
    {
        var pidLen = GetVarIntLength(packetId);
        var totalPayloadLen = pidLen + payloadLength;
        var frameLenLen = GetVarIntLength(totalPayloadLen);
        var frame = new byte[frameLenLen + totalPayloadLen];

        var offset = 0;
        offset += WriteVarInt(frame.AsSpan(offset), totalPayloadLen);
        offset += WriteVarInt(frame.AsSpan(offset), packetId);
        writePayload(frame.AsSpan(offset, payloadLength));
        return frame;
    }

    // ── Compression (Set Compression, Login S2C 0x03) ────

    /// <summary>
    /// Wraps a raw MC frame with the Set Compression frame format.
    /// - If rawFrame.Length &lt; threshold: [VarInt(0)] + rawFrame
    /// - Otherwise: [VarInt(rawFrame.Length)] + zlib(rawFrame)
    /// </summary>
    public static byte[] WrapCompressed(byte[] rawFrame, int threshold)
    {
        if (rawFrame.Length < threshold)
        {
            var prefixLen = GetVarIntLength(0);
            var result = new byte[prefixLen + rawFrame.Length];
            var off = 0;
            off += WriteVarInt(result.AsSpan(off), 0);
            System.Buffer.BlockCopy(rawFrame, 0, result, off, rawFrame.Length);
            return result;
        }

        using var output = new MemoryStream(rawFrame.Length);
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(rawFrame.AsSpan());
        }

        var compressed = output.ToArray();
        var dataLenLen = GetVarIntLength(rawFrame.Length);
        var result2 = new byte[dataLenLen + compressed.Length];
        var off2 = 0;
        off2 += WriteVarInt(result2.AsSpan(off2), rawFrame.Length);
        System.Buffer.BlockCopy(compressed, 0, result2, off2, compressed.Length);
        return result2;
    }
}
