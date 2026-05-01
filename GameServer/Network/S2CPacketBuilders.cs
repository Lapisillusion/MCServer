using System.Text;

namespace GameServer.Network;

/// <summary>
/// Minecraft 1.12.2 (protocol 340) S2C Play packet constructors.
/// Each method returns a complete MC frame ready to write to the stream.
/// </summary>
public static class S2CPacketBuilders
{
    // S2C Play packet IDs (protocol 340)
    private const int S2C_JoinGame = 0x23;
    private const int S2C_PluginMessage = 0x18;
    private const int S2C_ServerDifficulty = 0x0D;
    private const int S2C_PlayerAbilities = 0x2C;
    private const int S2C_SpawnPosition = 0x46;
    private const int S2C_PlayerPositionAndLook = 0x2F;
    private const int S2C_KeepAlive = 0x1F;
    private const int S2C_ChunkData = 0x20;

    /// <summary>Send a complete MC frame to the stream.</summary>
    public static async Task SendPacketAsync(Stream stream, byte[] frame, CancellationToken ct = default)
    {
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    public static byte[] BuildJoinGame(int entityId, byte gamemode, int dimension, byte difficulty,
        byte maxPlayers, string levelType, bool reducedDebugInfo)
    {
        var levelTypeBytes = Encoding.UTF8.GetBytes(levelType);
        var levelTypeLen = McProtocolWriter.GetVarIntLength(levelTypeBytes.Length) + levelTypeBytes.Length;
        var payloadLen = 4 + 1 + 4 + 1 + 1 + levelTypeLen + 1;
        return McProtocolWriter.BuildMcFrame(S2C_JoinGame, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteInt32(dst[off..], entityId);
            off += McProtocolWriter.WriteByte(dst[off..], gamemode);
            off += McProtocolWriter.WriteInt32(dst[off..], dimension);
            off += McProtocolWriter.WriteByte(dst[off..], difficulty);
            off += McProtocolWriter.WriteByte(dst[off..], maxPlayers);
            off += McProtocolWriter.WriteString(dst[off..], levelType);
            McProtocolWriter.WriteBool(dst[off..], reducedDebugInfo);
        }, payloadLen);
    }

    public static byte[] BuildPluginMessage(string channel, ReadOnlySpan<byte> data)
    {
        var channelLen = McProtocolWriter.GetStringLength(channel);
        var payloadLen = channelLen + data.Length;
        var pidLen = McProtocolWriter.GetVarIntLength(S2C_PluginMessage);
        var totalPayloadLen = pidLen + payloadLen;
        var frameLenLen = McProtocolWriter.GetVarIntLength(totalPayloadLen);
        var frame = new byte[frameLenLen + totalPayloadLen];
        var off = 0;
        off += McProtocolWriter.WriteVarInt(frame.AsSpan(off), totalPayloadLen);
        off += McProtocolWriter.WriteVarInt(frame.AsSpan(off), S2C_PluginMessage);
        off += McProtocolWriter.WriteString(frame.AsSpan(off), channel);
        data.CopyTo(frame.AsSpan(off));
        return frame;
    }

    public static byte[] BuildPluginMessage(string channel, string data)
    {
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var dataLen = McProtocolWriter.GetVarIntLength(dataBytes.Length) + dataBytes.Length;
        var channelLen = McProtocolWriter.GetStringLength(channel);
        var payloadLen = channelLen + dataLen;
        return McProtocolWriter.BuildMcFrame(S2C_PluginMessage, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteString(dst[off..], channel);
            McProtocolWriter.WriteString(dst[off..], data);
        }, payloadLen);
    }

    public static byte[] BuildServerDifficulty(byte difficulty)
    {
        return McProtocolWriter.BuildMcFrame(S2C_ServerDifficulty, dst =>
        {
            McProtocolWriter.WriteByte(dst, difficulty);
        }, 1);
    }

    public static byte[] BuildPlayerAbilities(byte flags, float flyingSpeed, float walkingSpeed)
    {
        return McProtocolWriter.BuildMcFrame(S2C_PlayerAbilities, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteByte(dst[off..], flags);
            off += McProtocolWriter.WriteFloat(dst[off..], flyingSpeed);
            McProtocolWriter.WriteFloat(dst[off..], walkingSpeed);
        }, 1 + 4 + 4);
    }

    public static byte[] BuildSpawnPosition(long x, long y, long z)
    {
        var encoded = McProtocolWriter.EncodePosition(x, y, z);
        return McProtocolWriter.BuildMcFrame(S2C_SpawnPosition, dst =>
        {
            McProtocolWriter.WriteInt64(dst, encoded);
        }, 8);
    }

    public static byte[] BuildPlayerPositionAndLook(double x, double y, double z,
        float yaw, float pitch, byte flags, int teleportId)
    {
        var tpIdLen = McProtocolWriter.GetVarIntLength(teleportId);
        var payloadLen = 8 + 8 + 8 + 4 + 4 + 1 + tpIdLen;
        return McProtocolWriter.BuildMcFrame(S2C_PlayerPositionAndLook, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteDouble(dst[off..], x);
            off += McProtocolWriter.WriteDouble(dst[off..], y);
            off += McProtocolWriter.WriteDouble(dst[off..], z);
            off += McProtocolWriter.WriteFloat(dst[off..], yaw);
            off += McProtocolWriter.WriteFloat(dst[off..], pitch);
            off += McProtocolWriter.WriteByte(dst[off..], flags);
            McProtocolWriter.WriteVarInt(dst[off..], teleportId);
        }, payloadLen);
    }

    public static byte[] BuildKeepAlive(long keepAliveId)
    {
        return McProtocolWriter.BuildMcFrame(S2C_KeepAlive, dst =>
        {
            McProtocolWriter.WriteInt64(dst, keepAliveId);
        }, 8);
    }

    public static byte[] BuildChunkData(int chunkX, int chunkZ, bool groundUp,
        int primaryBitMask, byte[] compressedData)
    {
        var bitMaskLen = McProtocolWriter.GetVarIntLength(primaryBitMask);
        var dataLen = McProtocolWriter.GetVarIntLength(compressedData.Length);
        var blockEntitiesLen = 1; // VarInt(0) = no block entities
        var payloadLen = 4 + 4 + 1 + bitMaskLen + dataLen + compressedData.Length + blockEntitiesLen;
        return McProtocolWriter.BuildMcFrame(S2C_ChunkData, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteInt32(dst[off..], chunkX);
            off += McProtocolWriter.WriteInt32(dst[off..], chunkZ);
            off += McProtocolWriter.WriteBool(dst[off..], groundUp);
            off += McProtocolWriter.WriteVarInt(dst[off..], primaryBitMask);
            off += McProtocolWriter.WriteVarInt(dst[off..], compressedData.Length);
            compressedData.CopyTo(dst[off..]);
            off += compressedData.Length;
            McProtocolWriter.WriteVarInt(dst[off..], 0); // block entities count = 0
        }, payloadLen);
    }
}
