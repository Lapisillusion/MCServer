// VarInt.cs

namespace GateWay;

public static class VarInt
{
    /// <summary>
    /// 从src中读取一个VarInt
    /// </summary>
    /// <param name="src">源</param>
    /// <param name="offset">返回读取varint后的指针index</param>
    /// <param name="value">varint值</param>
    /// <returns>是否成功</returns>
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

            var read = src[offset++];
            var v = read & 0b0111_1111;
            result |= v << (7 * numRead);

            numRead++;
            if (numRead > 5)
            {
                value = 0;
                return false;
            } // malicious

            if ((read & 0b1000_0000) == 0) break;
        }

        value = result;
        return true;
    }

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
}