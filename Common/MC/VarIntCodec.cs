namespace Common.MC;

/// <summary>
/// Minecraft VarInt / VarLong encoding for protocol 340.
/// Each byte carries 7 data bits + 1 continuation bit.
/// VarInt: 1-5 bytes, VarLong: 1-10 bytes (same algorithm).
/// </summary>
public static class VarIntCodec
{
    /// <summary>Read a VarInt from the span. Returns false if incomplete or malformed.</summary>
    public static bool TryRead(ReadOnlySpan<byte> src, ref int offset, out int value)
    {
        value = 0;
        var numRead = 0;
        var result = 0;

        while (true)
        {
            if (offset >= src.Length)
            {
                value = 0;
                return false;
            }

            var b = src[offset++];
            var payload = b & 0b0111_1111;
            result |= payload << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                value = 0;
                return false;
            } // malicious / overflow

            if ((b & 0b1000_0000) == 0)
                break;
        }

        value = result;
        return true;
    }

    /// <summary>Write a VarInt to the span. Returns bytes written.</summary>
    public static int Write(Span<byte> dst, int value)
    {
        var v = (uint)value;
        var i = 0;
        while (true)
        {
            if ((v & ~0x7Fu) == 0)
            {
                dst[i++] = (byte)v;
                return i;
            }

            dst[i++] = (byte)((v & 0x7F) | 0x80);
            v >>= 7;
        }
    }

    /// <summary>Number of bytes needed to encode a VarInt value.</summary>
    public static int GetLength(int value)
    {
        var v = (uint)value;
        var len = 0;
        while (true)
        {
            len++;
            if ((v & ~0x7Fu) == 0)
                return len;
            v >>= 7;
        }
    }
}
