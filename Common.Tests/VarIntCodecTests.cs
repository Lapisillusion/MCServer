using Common.MC;
using Xunit;

namespace Common.Tests;

public class VarIntCodecTests
{
    [Fact]
    public void Write_Zero_ReturnsOneByte()
    {
        var dst = new byte[5];
        var written = VarIntCodec.Write(dst, 0);
        Assert.Equal(1, written);
        Assert.Equal(0, dst[0]);
    }

    [Fact]
    public void Write_127_ReturnsOneByte()
    {
        var dst = new byte[5];
        var written = VarIntCodec.Write(dst, 127);
        Assert.Equal(1, written);
        Assert.Equal(0x7F, dst[0]);
    }

    [Fact]
    public void Write_128_ReturnsTwoBytes()
    {
        var dst = new byte[5];
        var written = VarIntCodec.Write(dst, 128);
        Assert.Equal(2, written);
        Assert.Equal(0x80, dst[0]);
        Assert.Equal(0x01, dst[1]);
    }

    [Fact]
    public void Write_NegativeOne_ReturnsFiveBytes()
    {
        var dst = new byte[5];
        var written = VarIntCodec.Write(dst, -1);
        Assert.Equal(5, written);
    }

    [Fact]
    public void RoundTrip_MaxInt()
    {
        RoundTrip(int.MaxValue);
    }

    [Fact]
    public void RoundTrip_MinInt()
    {
        RoundTrip(int.MinValue);
    }

    [Fact]
    public void RoundTrip_CommonValues()
    {
        RoundTrip(0);
        RoundTrip(1);
        RoundTrip(-1);
        RoundTrip(127);
        RoundTrip(128);
        RoundTrip(255);
        RoundTrip(256);
        RoundTrip(16383);
        RoundTrip(16384);
        RoundTrip(2097151);
        RoundTrip(2097152);
        RoundTrip(268435455);
        RoundTrip(268435456);
    }

    [Fact]
    public void TryRead_IncompleteData_ReturnsFalse()
    {
        var data = new byte[] { 0x80 };
        var off = 0;
        var result = VarIntCodec.TryRead(data, ref off, out var value);
        Assert.False(result);
        Assert.Equal(0, value);
        Assert.True(off > 0);
    }

    [Fact]
    public void TryRead_LegalData_ReturnsTrue()
    {
        var data = new byte[] { 0x80, 0x01 };
        var off = 0;
        var result = VarIntCodec.TryRead(data, ref off, out var value);
        Assert.True(result);
        Assert.Equal(128, value);
        Assert.Equal(2, off);
    }

    [Fact]
    public void GetLength_CorrectForAllRanges()
    {
        Assert.Equal(1, VarIntCodec.GetLength(0));
        Assert.Equal(1, VarIntCodec.GetLength(127));
        Assert.Equal(2, VarIntCodec.GetLength(128));
        Assert.Equal(2, VarIntCodec.GetLength(16383));
        Assert.Equal(3, VarIntCodec.GetLength(16384));
        Assert.Equal(3, VarIntCodec.GetLength(2097151));
        Assert.Equal(4, VarIntCodec.GetLength(2097152));
        Assert.Equal(5, VarIntCodec.GetLength(-1));
    }

    private static void RoundTrip(int value)
    {
        var dst = new byte[5];
        var written = VarIntCodec.Write(dst, value);
        var off = 0;
        var ok = VarIntCodec.TryRead(dst.AsSpan(0, written), ref off, out var result);
        Assert.True(ok);
        Assert.Equal(value, result);
    }
}
