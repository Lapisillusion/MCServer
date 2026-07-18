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
    // ── M3 multiplayer visibility ────────────────────────────
    private const int S2C_SpawnPlayer = Protocol340Ids.PlayS2C.SpawnPlayer;
    private const int S2C_PlayerListItem = Protocol340Ids.PlayS2C.PlayerListItem;
    private const int S2C_DestroyEntities = Protocol340Ids.PlayS2C.DestroyEntities;
    private const int S2C_EntityRelativeMove = Protocol340Ids.PlayS2C.EntityRelativeMove;
    private const int S2C_EntityLookAndRelativeMove = Protocol340Ids.PlayS2C.EntityLookAndRelativeMove;
    private const int S2C_EntityLook = Protocol340Ids.PlayS2C.EntityLook;
    private const int S2C_Entity = Protocol340Ids.PlayS2C.Entity;
    private const int S2C_EntityMetadata = Protocol340Ids.PlayS2C.EntityMetadata;

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

    // ── M3: Multiplayer Visibility ───────────────────────

    /// <summary>S2C Spawn Player (0x05). Spawns another player entity on a client.
    /// 1.12.2 format: EntityId(UUID)XYZ Yaw Pitch + 0xFF metadata terminator (no Current Item).</summary>
    public static byte[] BuildSpawnPlayer(int entityId, string playerUuid, double x, double y, double z,
        byte yaw, byte pitch)
    {
        var uuidBytes = ParseUuid(playerUuid);
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 16 + 8 + 8 + 8 + 1 + 1 + 1; // +1 for metadata terminator 0xFF
        return McProtocolWriter.BuildMcFrame(S2C_SpawnPlayer, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            uuidBytes.CopyTo(dst[off..]); off += 16;
            off += McProtocolWriter.WriteDouble(dst[off..], x);
            off += McProtocolWriter.WriteDouble(dst[off..], y);
            off += McProtocolWriter.WriteDouble(dst[off..], z);
            off += McProtocolWriter.WriteByte(dst[off..], yaw);
            off += McProtocolWriter.WriteByte(dst[off..], pitch);
            // Entity metadata terminator
            McProtocolWriter.WriteByte(dst[off..], 0xFF);
        }, payloadLen);
    }

    /// <summary>
    /// S2C Player List Item (0x2E). Action 0=add, 4=remove.
    /// For add: sends UUID, name, gamemode, ping.
    /// For remove: sends UUID only.
    /// </summary>
    public static byte[] BuildPlayerListItemAdd(string playerUuid, string playerName, int gamemode, int ping)
    {
        var uuidBytes = ParseUuid(playerUuid);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(playerName);
        var nameLen = McProtocolWriter.GetVarIntLength(nameBytes.Length) + nameBytes.Length;
        var gmLen = McProtocolWriter.GetVarIntLength(gamemode);
        var pingLen = McProtocolWriter.GetVarIntLength(ping);

        var payloadLen = 1                                                          // action VarInt(0)
                       + 1                                                          // count VarInt(1)
                       + 16                                                         // UUID
                       + nameLen                                                    // player name
                       + 1                                                          // properties VarInt(0)
                       + gmLen                                                      // gamemode VarInt
                       + pingLen                                                    // ping VarInt
                       + 1;                                                         // hasDisplayName bool(false)

        return McProtocolWriter.BuildMcFrame(S2C_PlayerListItem, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], 0); // action: add
            off += McProtocolWriter.WriteVarInt(dst[off..], 1); // count: 1 player
            uuidBytes.CopyTo(dst[off..]); off += 16;
            off += McProtocolWriter.WriteString(dst[off..], playerName);
            off += McProtocolWriter.WriteVarInt(dst[off..], 0); // properties: 0
            off += McProtocolWriter.WriteVarInt(dst[off..], gamemode);
            off += McProtocolWriter.WriteVarInt(dst[off..], ping);
            McProtocolWriter.WriteBool(dst[off..], false); // hasDisplayName
        }, payloadLen);
    }

    /// <summary>S2C Player List Item (0x2E) remove action.</summary>
    public static byte[] BuildPlayerListItemRemove(string playerUuid)
    {
        var uuidBytes = ParseUuid(playerUuid);
        var payloadLen = 1 + 1 + 16; // action(4) + count(1) + UUID
        return McProtocolWriter.BuildMcFrame(S2C_PlayerListItem, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], 4); // action: remove
            off += McProtocolWriter.WriteVarInt(dst[off..], 1); // count: 1
            uuidBytes.CopyTo(dst[off..]);
        }, payloadLen);
    }

    /// <summary>S2C Destroy Entities (0x32). Removes entities by ID from the client.</summary>
    public static byte[] BuildDestroyEntities(int[] entityIds)
    {
        var countLen = McProtocolWriter.GetVarIntLength(entityIds.Length);
        var idsLen = 0;
        foreach (var id in entityIds)
            idsLen += McProtocolWriter.GetVarIntLength(id);

        var payloadLen = countLen + idsLen;
        return McProtocolWriter.BuildMcFrame(S2C_DestroyEntities, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityIds.Length);
            foreach (var id in entityIds)
                off += McProtocolWriter.WriteVarInt(dst[off..], id);
        }, payloadLen);
    }

    /// <summary>S2C Entity Relative Move (0x26). Efficient delta position broadcast.</summary>
    public static byte[] BuildEntityRelativeMove(int entityId, short dx, short dy, short dz, bool onGround)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 2 + 2 + 2 + 1;
        return McProtocolWriter.BuildMcFrame(S2C_EntityRelativeMove, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            off += WriteShort(dst[off..], dx);
            off += WriteShort(dst[off..], dy);
            off += WriteShort(dst[off..], dz);
            McProtocolWriter.WriteBool(dst[off..], onGround);
        }, payloadLen);
    }

    /// <summary>S2C Entity Look And Relative Move (0x27). Delta position + rotation.</summary>
    public static byte[] BuildEntityLookAndRelativeMove(int entityId, short dx, short dy, short dz,
        byte yaw, byte pitch, bool onGround)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 2 + 2 + 2 + 1 + 1 + 1;
        return McProtocolWriter.BuildMcFrame(S2C_EntityLookAndRelativeMove, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            off += WriteShort(dst[off..], dx);
            off += WriteShort(dst[off..], dy);
            off += WriteShort(dst[off..], dz);
            off += McProtocolWriter.WriteByte(dst[off..], yaw);
            off += McProtocolWriter.WriteByte(dst[off..], pitch);
            McProtocolWriter.WriteBool(dst[off..], onGround);
        }, payloadLen);
    }

    /// <summary>S2C Entity Look (0x28). Rotation-only update.</summary>
    public static byte[] BuildEntityLook(int entityId, byte yaw, byte pitch, bool onGround)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 1 + 1 + 1;
        return McProtocolWriter.BuildMcFrame(S2C_EntityLook, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            off += McProtocolWriter.WriteByte(dst[off..], yaw);
            off += McProtocolWriter.WriteByte(dst[off..], pitch);
            McProtocolWriter.WriteBool(dst[off..], onGround);
        }, payloadLen);
    }

    /// <summary>S2C Entity (0x25). Ground-state-only update (no position/rotation change).</summary>
    public static byte[] BuildEntity(int entityId, bool onGround)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + 1;
        return McProtocolWriter.BuildMcFrame(S2C_Entity, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            McProtocolWriter.WriteBool(dst[off..], onGround);
        }, payloadLen);
    }

    /// <summary>S2C Entity Metadata (0x3C). Sends entity metadata (e.g. skin flags).</summary>
    public static byte[] BuildEntityMetadata(int entityId, byte[] metadata)
    {
        var eidLen = McProtocolWriter.GetVarIntLength(entityId);
        var payloadLen = eidLen + metadata.Length;
        return McProtocolWriter.BuildMcFrame(S2C_EntityMetadata, dst =>
        {
            var off = 0;
            off += McProtocolWriter.WriteVarInt(dst[off..], entityId);
            metadata.CopyTo(dst[off..]);
        }, payloadLen);
    }

    // ── Helpers ───────────────────────────────────────────

    /// <summary>Convert float angle (degrees) to protocol byte (0-255).</summary>
    private static byte AngleToByte(float angle)
    {
        var wrapped = angle % 360f;
        if (wrapped < 0) wrapped += 360f;
        return (byte)(wrapped * 256f / 360f);
    }

    /// <summary>Encode a double position delta to a protocol short (delta * 32 * 128).</summary>
    public static short EncodeDelta(double current, double previous)
    {
        var delta = (current - previous) * 32.0 * 128.0;
        return (short)Math.Clamp(delta, short.MinValue, short.MaxValue);
    }

    /// <summary>Write a 16-bit big-endian short to the span.</summary>
    private static int WriteShort(Span<byte> dst, short value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(dst, value);
        return 2;
    }

    /// <summary>Parse a UUID string "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" to 16 raw bytes.</summary>
    private static byte[] ParseUuid(string uuid)
    {
        var hex = uuid.Replace("-", "");
        var bytes = new byte[16];
        for (var i = 0; i < 16; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    /// <summary>
    /// S2C Entity Teleport (0x4C). Force-syncs player position.
    /// (Copied from prior location with updated helpers)
    /// </summary>
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
}
