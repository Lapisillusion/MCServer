using System.IO.Compression;
using GameServer.Network;

namespace GameServer.World;

/// <summary>
/// Builds 1.12.2 format chunk data for a classic superflat world.
/// Layers: y=0 bedrock, y=1-2 dirt, y=3 grass, y=4-15 air.
/// Only section 0 (y=0..15) is present; section 0 primary bitmask = 0x01.
/// Biome = Plains (1) for all 256 columns.
/// </summary>
public static class SuperflatChunkBuilder
{
    private const int BlockCount = 16 * 16 * 16; // 4096
    private const int NibbleCount = BlockCount / 2; // 2048
    private const int SectionDataSize = BlockCount + NibbleCount * 3; // 10240
    private const int BiomeDataSize = 256;

    private const byte Block_Bedrock = 7;
    private const byte Block_Dirt = 3;
    private const byte Block_Grass = 2;
    private const byte Block_Air = 0;

    private static byte[]? _cachedSection;
    private static byte[]? _cachedBiomes;

    /// <summary>Y-index within the section (0..15).</summary>
    private static int BlockIndex(int x, int y, int z) => (y << 8) | (z << 4) | x;

    private static byte[] BuildSection0()
    {
        if (_cachedSection != null)
            return _cachedSection;

        var data = new byte[SectionDataSize];
        var blockIds = data.AsSpan(0, BlockCount);
        var metadata = data.AsSpan(BlockCount, NibbleCount);
        var blockLight = data.AsSpan(BlockCount + NibbleCount, NibbleCount);
        var skyLight = data.AsSpan(BlockCount + NibbleCount * 2, NibbleCount);

        // Fill blocks by Y layer
        for (var y = 0; y < 16; y++)
        {
            byte blockId = y switch
            {
                0 => Block_Bedrock,
                1 or 2 => Block_Dirt,
                3 => Block_Grass,
                _ => Block_Air
            };

            for (var z = 0; z < 16; z++)
            for (var x = 0; x < 16; x++)
            {
                blockIds[BlockIndex(x, y, z)] = blockId;
            }
        }

        // Metadata: all 0 (no subtypes)
        // BlockLight: all 0 (no light-emitting blocks)
        // SkyLight: all 0xFF (full skylight everywhere — each byte = two 0xF nibbles)
        skyLight.Fill(0xFF);

        _cachedSection = data;
        return data;
    }

    private static byte[] BuildBiomeData()
    {
        if (_cachedBiomes != null)
            return _cachedBiomes;

        var biomes = new byte[BiomeDataSize];
        Array.Fill(biomes, (byte)1); // Plains
        _cachedBiomes = biomes;
        return biomes;
    }

    /// <summary>
    /// Build and compress chunk data for one chunk column.
    /// Returns the compressed byte array (zlib format).
    /// </summary>
    private static byte[] CompressChunkData()
    {
        var section = BuildSection0();
        var biomes = BuildBiomeData();
        var uncompressed = new byte[section.Length + biomes.Length];
        section.CopyTo(uncompressed, 0);
        biomes.CopyTo(uncompressed, section.Length);

        using var memStream = new MemoryStream();
        using (var zlib = new ZLibStream(memStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(uncompressed, 0, uncompressed.Length);
        }

        return memStream.ToArray();
    }

    /// <summary>Build a complete S2C Chunk Data frame for a superflat chunk.</summary>
    public static byte[] BuildChunkPacket(int chunkX, int chunkZ, int primaryBitMask = 0x01)
    {
        var compressed = CompressChunkData();
        return S2CPacketBuilders.BuildChunkData(chunkX, chunkZ, groundUp: true, primaryBitMask, compressed);
    }
}
