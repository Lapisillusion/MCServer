using System.Text;
using Common;

namespace GateWay;

public static class McFrameParser
{
    public const int MaxFramePayload = 2 * 1024 * 1024;
    public const int MaxStringLen = 255;
    public const int MaxNameLen = 16;

    public readonly record struct HandshakePacket(int ProtocolVersion, string ServerAddress, ushort ServerPort, int NextState);
    public readonly record struct LoginStartPacket(string Username);

    public static bool TryReadFrame(ByteRingBuffer rb, Span<byte> temp, out int frameLenTotal)
    {
        frameLenTotal = 0;

        var peek = rb.Peek(temp, Math.Min(temp.Length, Math.Min(rb.Count, 5)));
        if (peek == 0)
            return false;

        var off = 0;
        if (!VarInt.TryRead(temp[..peek], ref off, out var payloadLen))
            return false;

        if (payloadLen < 0 || payloadLen > MaxFramePayload)
            throw new InvalidOperationException("Payload too large");

        var needTotal = off + payloadLen;
        if (rb.Count < needTotal)
            return false;

        frameLenTotal = needTotal;
        return true;
    }

    public static bool TryGetPacketId(ReadOnlySpan<byte> frameSpan, out int packetId, out int payloadOffset)
    {
        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out _))
        {
            packetId = 0;
            payloadOffset = 0;
            return false;
        }

        payloadOffset = off;
        if (!VarInt.TryRead(frameSpan, ref off, out packetId))
        {
            packetId = 0;
            payloadOffset = 0;
            return false;
        }

        return true;
    }

    public static bool TryParseHandshakeNextState(ReadOnlySpan<byte> frameSpan, out int nextState)
    {
        if (!TryParseHandshake(frameSpan, out var handshake))
        {
            nextState = 0;
            return false;
        }

        nextState = handshake.NextState;
        return true;
    }

    public static bool TryParseHandshake(ReadOnlySpan<byte> frameSpan, out HandshakePacket handshake)
    {
        handshake = default;

        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out var payloadLen))
            return false;
        if (payloadLen < 0 || payloadLen > MaxFramePayload)
            return false;
        var payloadEnd = off + payloadLen;
        if (payloadEnd != frameSpan.Length)
            return false;

        if (!VarInt.TryRead(frameSpan, ref off, out var pid))
            return false;
        if (pid != Protocol340Ids.C2S_Handshake)
            return false;

        if (!VarInt.TryRead(frameSpan, ref off, out var protocolVersion))
            return false;
        if (!TryReadString(frameSpan, ref off, MaxStringLen, out var serverAddress))
            return false;

        if (off + 2 > frameSpan.Length)
            return false;
        var serverPort = (ushort)((frameSpan[off] << 8) | frameSpan[off + 1]);
        off += 2;

        if (!VarInt.TryRead(frameSpan, ref off, out var nextState))
            return false;
        if (nextState is not (1 or 2))
            return false;
        if (off != payloadEnd)
            return false;

        handshake = new HandshakePacket(protocolVersion, serverAddress, serverPort, nextState);
        return true;
    }

    public static bool TryValidateLoginStart(ReadOnlySpan<byte> frameSpan)
    {
        return TryParseLoginStart(frameSpan, out _);
    }

    public static bool TryParseLoginStart(ReadOnlySpan<byte> frameSpan, out LoginStartPacket loginStart)
    {
        loginStart = default;

        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out var payloadLen))
            return false;
        if (payloadLen < 0 || payloadLen > MaxFramePayload)
            return false;
        var payloadEnd = off + payloadLen;
        if (payloadEnd != frameSpan.Length)
            return false;

        if (!VarInt.TryRead(frameSpan, ref off, out var pid))
            return false;
        if (pid != Protocol340Ids.C2S_LoginStart)
            return false;

        if (!TryReadString(frameSpan, ref off, MaxNameLen, out var userName))
            return false;
        if (userName.Length is < 1 or > MaxNameLen)
            return false;
        if (!IsValidPlayerName(userName))
            return false;
        if (off != payloadEnd)
            return false;

        loginStart = new LoginStartPacket(userName);
        return true;
    }

    public static bool TryIsStatusRequest(ReadOnlySpan<byte> frameSpan)
    {
        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out var payloadLen))
            return false;
        if (payloadLen != 1)
            return false;
        if (!VarInt.TryRead(frameSpan, ref off, out var pid))
            return false;
        return pid == Protocol340Ids.C2S_StatusRequest && off == frameSpan.Length;
    }

    public static bool TryReadStatusPingPayload(ReadOnlySpan<byte> frameSpan, out long payload)
    {
        payload = 0;

        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out var payloadLen))
            return false;
        if (!VarInt.TryRead(frameSpan, ref off, out var pid))
            return false;
        if (pid != Protocol340Ids.C2S_StatusPing)
            return false;
        if (payloadLen != 9)
            return false;
        if (off + 8 != frameSpan.Length)
            return false;

        ulong value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | frameSpan[off + i];

        payload = unchecked((long)value);
        return true;
    }

    private static bool TryReadString(ReadOnlySpan<byte> src, ref int off, int maxLen, out string value)
    {
        value = string.Empty;
        if (!VarInt.TryRead(src, ref off, out var strLen))
            return false;
        if (strLen < 0 || strLen > maxLen)
            return false;
        if (off + strLen > src.Length)
            return false;

        value = Encoding.UTF8.GetString(src.Slice(off, strLen));
        off += strLen;
        return true;
    }

    private static bool IsValidPlayerName(string name)
    {
        foreach (var ch in name)
        {
            var isDigit = ch is >= '0' and <= '9';
            var isUpper = ch is >= 'A' and <= 'Z';
            var isLower = ch is >= 'a' and <= 'z';
            if (!isDigit && !isUpper && !isLower && ch != '_')
                return false;
        }

        return true;
    }
}
