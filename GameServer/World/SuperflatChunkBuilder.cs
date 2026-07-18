using System.Buffers.Binary;
using GameServer.Core.Diagnostics;
using GameServer.Network;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.World;

/// <summary>
/// Builds 1.12.2 (protocol 340) format chunk data for a classic superflat world.
/// Layers: y=0 bedrock, y=1-2 dirt, y=3 grass, y=4-15 air.
///
/// Section format (1.9+):
///   [bits_per_block: u8] [palette_len: VarInt] [palette: VarInt[]]
///   [data_array_len: VarInt] [data_array: packed u64[] LSB-first, big-endian per long]
///   [block_light: 2048B nibbles] [sky_light: 2048B nibbles]
///
/// Block states array stores actual 1.12.2 block state values
/// (state = (block_id << 4) | metadata), NOT palette indices.
/// SerializeSection dynamically builds the palette from the actual unique values present.
///
/// Data is NOT zlib-compressed here — protocol-level compression (Set Compression)
/// handles that separately. The Chunk Data packet carries raw section + biome bytes.
///
/// Only section 0 is sent (primaryBitMask=0x01). Biome=Plains(1) for all columns.
/// </summary>
public static class SuperflatChunkBuilder
{
    private const int BlockCount = 16 * 16 * 16; // 4096
    private const int NibbleCount = BlockCount / 2; // 2048
    private const int BiomeDataSize = 256;

    // Block states for 1.12.2 (pre-flattening): state = (block_id << 4) | metadata
    public const int State_Air = 0;       // (0 << 4) | 0
    public const int State_Grass = 32;    // (2 << 4) | 0
    public const int State_Dirt = 48;     // (3 << 4) | 0
    public const int State_Bedrock = 112; // (7 << 4) | 0

    // Shared cached data — all superflat sections are identical.
    private static byte[]? _cachedBlockStates;
    private static byte[]? _cachedBlockLight;
    private static byte[]? _cachedSkyLight;
    private static byte[]? _cachedBiomes;
    private static bool _loggedStructure;

    /// <summary>Y-index within the section (0..15), YZX ordering.</summary>
    public static int BlockIndex(int x, int y, int z) => (y << 8) | (z << 4) | x;

    /// <summary>
    /// Returns 4096 block STATE values (NOT palette indices) for the given section Y.
    /// Values are 1.12.2 block states: state = (block_id << 4) | metadata.
    /// </summary>
    public static byte[] GetSectionBlockStates(int sectionY)
    {
        if (_cachedBlockStates != null)
            return _cachedBlockStates;

        var states = new byte[BlockCount];
        var baseY = sectionY * 16;

        for (var y = 0; y < 16; y++)
        {
            byte blockState = (baseY + y) switch
            {
                0 => (byte)State_Bedrock,
                1 or 2 => (byte)State_Dirt,
                3 => (byte)State_Grass,
                _ => (byte)State_Air
            };

            for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++)
            {
                states[BlockIndex(x, y, z)] = blockState;
            }
        }

        _cachedBlockStates = states;
        return states;
    }

    /// <summary>Returns 2048 nibble bytes, all 0xFF (full brightness).</summary>
    public static byte[] GetBlockLight()
    {
        if (_cachedBlockLight != null)
            return _cachedBlockLight;
        var light = new byte[NibbleCount];
        light.AsSpan().Fill(0xFF);
        _cachedBlockLight = light;
        return light;
    }

    /// <summary>Returns 2048 nibble bytes, all 0xFF (full skylight).</summary>
    public static byte[] GetSkyLight()
    {
        if (_cachedSkyLight != null)
            return _cachedSkyLight;
        var light = new byte[NibbleCount];
        light.AsSpan().Fill(0xFF);
        _cachedSkyLight = light;
        return light;
    }

    /// <summary>Returns 256-byte biome array, all Plains(1).</summary>
    public static byte[] GetBiomeData()
    {
        if (_cachedBiomes != null)
            return _cachedBiomes;
        var biomes = new byte[BiomeDataSize];
        Array.Fill(biomes, (byte)1); // Plains
        _cachedBiomes = biomes;
        return biomes;
    }

    /// <summary>
    /// Serializes block states + light into the 1.12.2 network section format.
    /// Dynamically builds the palette from the actual block state values present,
    /// so modifications (via SetBlock) are correctly reflected.
    ///
    /// Array values are 1.12.2 block states (not palette indices).
    /// </summary>
    public static byte[] SerializeSection(byte[] blockStates, byte[] blockLight, byte[] skyLight)
    {
        // -- Build dynamic palette from unique block states present --
        var paletteMap = new Dictionary<byte, int>(); // blockState → paletteIndex
        for (var i = 0; i < BlockCount; i++)
        {
            var state = blockStates[i];
            if (!paletteMap.ContainsKey(state))
                paletteMap[state] = paletteMap.Count;
        }

        var palette = new int[paletteMap.Count];
        foreach (var (state, idx) in paletteMap)
            palette[idx] = state;

        var bitsPerBlock = Math.Max(4, (int)Math.Ceiling(Math.Log2(palette.Length)));
        // bits_per_block must be at least 4 per protocol; if palette has ≤1 entry, still 4

        // -- Pack block data (LSB-first within each long) --
        var totalBits = BlockCount * bitsPerBlock;
        var dataLongCount = (totalBits + 63) / 64;
        var dataLongs = new long[dataLongCount];

        for (var i = 0; i < BlockCount; i++)
        {
            var paletteIdx = paletteMap[blockStates[i]];
            var bitOffset = i * bitsPerBlock;
            var longIdx = bitOffset / 64;
            var bitInLong = bitOffset % 64;
            dataLongs[longIdx] |= (long)paletteIdx << bitInLong;
        }

        // -- Write section --
        var paletteLenByteSize = McProtocolWriter.GetVarIntLength(palette.Length);
        var paletteEntriesSize = 0;
        foreach (var s in palette)
            paletteEntriesSize += McProtocolWriter.GetVarIntLength(s);
        var dataLenVarIntSize = McProtocolWriter.GetVarIntLength(dataLongCount);
        var dataArrayBytes = dataLongCount * 8;

        var sectionSize = 1                       // bits_per_block
                        + paletteLenByteSize      // palette length VarInt
                        + paletteEntriesSize      // palette entries
                        + dataLenVarIntSize       // data array length VarInt
                        + dataArrayBytes          // packed long[] data
                        + NibbleCount * 2;        // block light + sky light

        var data = new byte[sectionSize];
        var off = 0;

        // 1. Bits per block (u8)
        data[off++] = (byte)bitsPerBlock;

        // 2. Palette length (VarInt)
        off += McProtocolWriter.WriteVarInt(data.AsSpan(off), palette.Length);

        // 3. Palette entries (VarInt each), in palette index order
        for (var i = 0; i < palette.Length; i++)
            off += McProtocolWriter.WriteVarInt(data.AsSpan(off), palette[i]);

        // 4. Data array length (VarInt) — number of longs
        off += McProtocolWriter.WriteVarInt(data.AsSpan(off), dataLongCount);

        // 5. Data array (longs, big-endian)
        for (var i = 0; i < dataLongCount; i++)
        {
            BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(off, 8), dataLongs[i]);
            off += 8;
        }

        // 6. Block light
        blockLight.AsSpan().CopyTo(data.AsSpan(off, NibbleCount));
        off += NibbleCount;

        // 7. Sky light
        skyLight.AsSpan().CopyTo(data.AsSpan(off, NibbleCount));

        if (!_loggedStructure)
        {
            _loggedStructure = true;
            Info("ChunkDiag", $"Section={sectionSize}B, biomes={BiomeDataSize}B, total raw={sectionSize + BiomeDataSize}B");
            Info("ChunkDiag", $"bits_per_block={bitsPerBlock}, palette={palette.Length} entries, data_array={dataLongCount} longs ({dataArrayBytes}B)");
            Info("ChunkDiag", $"block_light=0xFF, sky_light=0xFF, biome=Plains(1)");
        }

        return data;
    }

    /// <summary>
    /// Build the raw (uncompressed) chunk data for the pristine superflat baseline.
    /// NOTE: This returns the ORIGINAL superflat data. For chunks that have been
    /// modified, use ChunkColumn.BuildChunkData() instead.
    /// </summary>
    public static byte[] GetRawChunkData()
    {
        // Always rebuild — the static block states array may be shared but
        // dynamic palette means the output could differ if blocks were modified.
        var section = SerializeSection(GetSectionBlockStates(0), GetBlockLight(), GetSkyLight());
        var biomes = GetBiomeData();
        var rawData = new byte[section.Length + biomes.Length];
        section.CopyTo(rawData, 0);
        biomes.CopyTo(rawData, section.Length);
        return rawData;
    }

    /// <summary>Build a complete S2C Chunk Data frame.</summary>
    public static byte[] BuildChunkPacket(int chunkX, int chunkZ, int primaryBitMask = 0x01)
    {
        var rawData = GetRawChunkData();
        return S2CPacketBuilders.BuildChunkData(chunkX, chunkZ, groundUp: true, primaryBitMask, rawData);
    }
}
