using System.Text;
using Common.MC;

namespace GameServer.Network;

/// <summary>
/// Minecraft 1.12.2 (protocol 340) S2C Play packet constructors.
/// Each method returns a complete MC frame ready to write to the stream.
/// Packet IDs from Common.MC.Protocol340Ids.
/// </summary>
public static class S2CPacketBuilders
{
    // S2C Play packet IDs (protocol 340) — aliased for brevity
    private const int S2C_JoinGame = Protocol340Ids.PlayS2C.JoinGame;
    private const int S2C_PluginMessage = Protocol340Ids.PlayS2C.PluginMessage;
    private const int S2C_ServerDifficulty = Protocol340Ids.PlayS2C.ServerDifficulty;
    private const int S2C_PlayerAbilities = Protocol340Ids.PlayS2C.PlayerAbilities;
    private const int S2C_SpawnPosition = Protocol340Ids.PlayS2C.SpawnPosition;
    private const int S2C_PlayerPositionAndLook = Protocol340Ids.PlayS2C.PlayerPositionAndLook;
    private const int S2C_KeepAlive = Protocol340Ids.PlayS2C.KeepAlive;
    private const int S2C_ChunkData = Protocol340Ids.PlayS2C.ChunkData;
    private const int S2C_SetCompression = Protocol340Ids.Login.S2C_SetCompression;

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
        int primaryBitMask, byte[] rawChunkData)
    {
        var bitMaskLen = McProtocolWriter.GetVarIntLength(primaryBitMask);
        var dataLen = McProtocolWriter.GetVarIntLength(rawChunkData.Length);
        var blockEntitiesLen = 1; // VarInt(0) = no block entities
        var payloadLen = 4 + 4 + 1 + bitMaskLen + dataLen + rawChunkData.Length + blockEntitiesLen;
        return McProtocolWriter.BuildMcFrame(S2C_ChunkData, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteInt32(dst[off..], chunkX);
            off += McProtocolWriter.WriteInt32(dst[off..], chunkZ);
            off += McProtocolWriter.WriteBool(dst[off..], groundUp);
            off += McProtocolWriter.WriteVarInt(dst[off..], primaryBitMask);
            off += McProtocolWriter.WriteVarInt(dst[off..], rawChunkData.Length);
            rawChunkData.CopyTo(dst[off..]);
            off += rawChunkData.Length;
            McProtocolWriter.WriteVarInt(dst[off..], 0); // block entities count = 0
        }, payloadLen);
    }

    // ── v0.3.0 Block Interaction ─────────────────────────

    /// <summary>S2C Block Change (0x0B). Encoded position + VarInt blockState.</summary>
    public static byte[] BuildBlockChange(int x, int y, int z, int blockState)
    {
        var encoded = McProtocolWriter.EncodePosition(x, y, z);
        var stateLen = McProtocolWriter.GetVarIntLength(blockState);
        var payloadLen = 8 + stateLen;
        return McProtocolWriter.BuildMcFrame(Protocol340Ids.PlayS2C.BlockChange, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteInt64(dst[off..], encoded);
            McProtocolWriter.WriteVarInt(dst[off..], blockState);
        }, payloadLen);
    }

    /// <summary>
    /// S2C Animation (0x06). Animation types: 0=swing main arm, 1=damage,
    /// 2=leave bed, 3=swing offhand, 4=critical effect, 5=magic critical.
    /// </summary>
    public static byte[] BuildAnimation(int entityId, byte animationType)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 1;
        return McProtocolWriter.BuildMcFrame(Protocol340Ids.PlayS2C.Animation, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            McProtocolWriter.WriteByte(dst[off..], animationType);
        }, payloadLen);
    }

    // ── v0.3.1 Movement Validation ────────────────────────

    /// <summary>S2C Entity Teleport (0x4C). Force-syncs player position.</summary>
    public static byte[] BuildEntityTeleport(int entityId, double x, double y, double z,
        float yaw, float pitch, bool onGround)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 8 + 8 + 8 + 1 + 1 + 1;
        return McProtocolWriter.BuildMcFrame(Protocol340Ids.PlayS2C.EntityTeleport, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            off += McProtocolWriter.WriteDouble(dst[off..], x);
            off += McProtocolWriter.WriteDouble(dst[off..], y);
            off += McProtocolWriter.WriteDouble(dst[off..], z);
            off += McProtocolWriter.WriteByte(dst[off..], AngleToByte(yaw));
            off += McProtocolWriter.WriteByte(dst[off..], AngleToByte(pitch));
            McProtocolWriter.WriteBool(dst[off..], onGround);
        }, payloadLen);
    }

    /// <summary>Convert float angle to protocol byte (wrapped 0-360 → 0-255).</summary>
    private static byte AngleToByte(float angle)
    {
        var wrapped = angle % 360f;
        if (wrapped < 0) wrapped += 360f;
        return (byte)(wrapped * 256f / 360f);
    }

    // ── Set Compression (Login S2C 0x03) ────────────────

    /// <summary>
    /// S2C Set Compression (0x03). Enables packet-level zlib compression.
    /// Sent during login, before Join Game. Packets >= threshold are compressed.
    /// </summary>
    public static byte[] BuildSetCompression(int threshold)
    {
        var threshLen = McProtocolWriter.GetVarIntLength(threshold);
        var payloadLen = threshLen;
        return McProtocolWriter.BuildMcFrame(S2C_SetCompression, dst =>
        {
            McProtocolWriter.WriteVarInt(dst, threshold);
        }, payloadLen);
    }
}
