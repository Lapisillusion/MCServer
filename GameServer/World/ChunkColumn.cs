namespace GameServer.World;

/// <summary>
/// A full chunk column: up to 16 sections (0-255 Y range) at a chunk position, plus biome data.
/// For superflat, only section 0 is populated; data arrays are shared across all columns.
/// </summary>
public sealed class ChunkColumn
{
    private readonly ChunkSection?[] _sections = new ChunkSection?[16];

    public ChunkPos Position { get; }
    public byte[] Biomes { get; private set; }
    public bool GroundUpContinuous { get; set; } = true;

    private byte[]? _cachedChunkData;
    private int _cachedPrimaryBitMask = -1;

    public ChunkColumn(ChunkPos position, byte[] biomes)
    {
        Position = position;
        Biomes = biomes;
    }

    /// <summary>
    /// Creates a superflat column at the given position.
    /// All section/light/biome data arrays are shared across columns — only the
    /// ChunkColumn wrapper (~48 bytes) is allocated per position.
    /// </summary>
    public static ChunkColumn CreateSuperflat(ChunkPos pos)
    {
        var column = new ChunkColumn(pos, SuperflatChunkBuilder.GetBiomeData());
        column.SetSection(ChunkSection.CreateSuperflat(0));
        return column;
    }

    public ChunkSection? GetSection(int yIndex)
        => (uint)yIndex < 16 ? _sections[yIndex] : null;

    /// <summary>Invalidate cached serialized chunk data (called after block modification).</summary>
    public void InvalidateChunkCache()
    {
        _cachedChunkData = null;
        _cachedPrimaryBitMask = -1;
    }

    public void SetSection(ChunkSection section)
    {
        if ((uint)section.Y >= 16)
            throw new ArgumentOutOfRangeException(nameof(section), $"Section Y={section.Y} out of range [0,15]");

        _sections[section.Y] = section;
        _cachedChunkData = null; // Invalidate
    }

    public ChunkSection GetOrCreateSection(int yIndex)
    {
        if ((uint)yIndex >= 16)
            throw new ArgumentOutOfRangeException(nameof(yIndex));

        var section = _sections[yIndex];
        if (section == null)
        {
            section = ChunkSection.CreateSuperflat(yIndex);
            _sections[yIndex] = section;
            _cachedChunkData = null;
        }
        return section;
    }

    /// <summary>
    /// Builds the raw (uncompressed) chunk data: enabled sections + biomes (optional).
    /// Cached per column; invalidated when sections change.
    /// </summary>
    public byte[] BuildChunkData(int primaryBitMask = 0x01, bool includeBiomes = true)
    {
        var cacheKey = primaryBitMask | (includeBiomes ? 0x10000 : 0);
        if (_cachedChunkData != null && _cachedPrimaryBitMask == cacheKey)
            return _cachedChunkData;

        var totalSize = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((primaryBitMask & (1 << i)) == 0)
                continue;

            var section = _sections[i];
            if (section == null)
                continue;

            totalSize += section.Serialize().Length;
        }

        if (includeBiomes && GroundUpContinuous)
            totalSize += Biomes.Length;

        var data = new byte[totalSize];
        var off = 0;

        for (var i = 0; i < 16; i++)
        {
            if ((primaryBitMask & (1 << i)) == 0)
                continue;

            var section = _sections[i];
            if (section == null) continue;
            var serialized = section.Serialize();
            Buffer.BlockCopy(serialized, 0, data, off, serialized.Length);
            off += serialized.Length;
        }

        if (includeBiomes && GroundUpContinuous)
            Buffer.BlockCopy(Biomes, 0, data, off, Biomes.Length);

        _cachedChunkData = data;
        _cachedPrimaryBitMask = cacheKey;
        return data;
    }
}
