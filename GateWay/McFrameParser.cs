// McFrameParser.cs

namespace GateWay;

public static class McFrameParser
{
    // 最大包长：MVP 建议 2MB（你可更严）
    public const int MaxFramePayload = 2 * 1024 * 1024;
    public const int MaxStringLen = 255;

    // 尝试从 ring buffer 中解析出一帧
    // 输出：frameBytes（包含 lengthVarInt + payload 的“原始帧”）
    // 注意：为了高性能，这里用 caller 提供的临时缓冲（stackalloc/租借）
    public static bool TryReadFrame(ByteRingBuffer rb, Span<byte> temp, out int frameLenTotal)
    {
        frameLenTotal = 0;

        // 至少要 peek 5 字节以解析 VarInt(length)
        var peek = rb.Peek(temp, Math.Min(temp.Length, Math.Min(rb.Count, 5)));
        if (peek == 0) return false;

        var off = 0;
        if (!VarInt.TryRead(temp[..peek], ref off, out var payloadLen))
            return false; // not enough or malformed

        if (payloadLen < 0 || payloadLen > MaxFramePayload)
            throw new InvalidOperationException("Payload too large");

        var needTotal = off + payloadLen; // varint bytes + payload bytes
        if (rb.Count < needTotal) return false;

        frameLenTotal = needTotal;
        return true;
    }

    // 读取 packetId（从 payload 的开头读 VarInt）
    // frameSpan 是 "lengthVarInt + payload"；payload 从 payloadOffset 开始
    public static bool TryGetPacketId(ReadOnlySpan<byte> frameSpan, out int packetId, out int payloadOffset)
    {
        // 先跳过 lengthVarInt
        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out var payloadLen))
        {
            packetId = 0;
            payloadOffset = 0;
            return false;
        }

        payloadOffset = off;

        // payload 开头读 packetId
        if (!VarInt.TryRead(frameSpan, ref off, out packetId))
        {
            packetId = 0;
            payloadOffset = 0;
            return false;
        }

        // off 现在指向 packet fields 起点（相对于 frameSpan）
        return true;
    }

    // 解析 Handshake，返回 nextState（1=Status,2=Login）
    // frameSpan: length+payload
    public static bool TryParseHandshakeNextState(ReadOnlySpan<byte> frameSpan, out int nextState)
    {
        nextState = 0;

        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out var payloadLen)) return false;

        // packetId
        if (!VarInt.TryRead(frameSpan, ref off, out var pid)) return false;
        if (pid != Protocol340Ids.C2S_Handshake) return false;

        // protocolVersion
        if (!VarInt.TryRead(frameSpan, ref off, out _)) return false;

        // serverAddress (string)
        if (!VarInt.TryRead(frameSpan, ref off, out var strLen)) return false;
        if (strLen < 0 || strLen > MaxStringLen) return false;
        if (off + strLen > frameSpan.Length) return false;
        off += strLen;

        // serverPort ushort
        if (off + 2 > frameSpan.Length) return false;
        off += 2;

        // nextState varint
        if (!VarInt.TryRead(frameSpan, ref off, out nextState)) return false;
        if (nextState != 1 && nextState != 2) return false;

        return true;
    }

    // （可选）解析 LoginStart 的 username 做长度校验
    public static bool TryValidateLoginStart(ReadOnlySpan<byte> frameSpan)
    {
        var off = 0;
        if (!VarInt.TryRead(frameSpan, ref off, out _)) return false;
        if (!VarInt.TryRead(frameSpan, ref off, out var pid)) return false;
        if (pid != Protocol340Ids.C2S_LoginStart) return false;

        if (!VarInt.TryRead(frameSpan, ref off, out var nameLen)) return false;
        if (nameLen < 1 || nameLen > 16) return false; // MC 用户名常见限制
        if (off + nameLen > frameSpan.Length) return false;
        // 可进一步校验字符集
        return true;
    }
}