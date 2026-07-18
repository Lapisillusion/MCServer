namespace GameServer.World;

/// <summary>
/// Block storage for one 16×16×16 chunk section (Y index 0..15).
/// Uses shared cached arrays from SuperflatChunkBuilder for the immutable superflat case.
/// When v0.3.0 adds block modification, CopyOnWrite will create per-section arrays.
/// </summary>
public sealed class ChunkSection
{
    private byte[] _blockStates;
    private byte[] _blockLight;
    private byte[] _skyLight;
    private byte[]? _serialized;
    private bool _dirty;

    /// <summary>Section Y index (0..15).</summary>
    public int Y { get; }

    /// <summary>True if blocks have been modified since last serialization.</summary>
    public bool IsDirty => _dirty;

    public ChunkSection(int y, byte[] blockStates, byte[] blockLight, byte[] skyLight)
    {
        Y = y;
        _blockStates = blockStates;
        _blockLight = blockLight;
        _skyLight = skyLight;
    }

    /// <summary>
    /// Creates a superflat section for the given Y index using shared cached data.
    /// No per-section allocation beyond the ChunkSection object itself (~32 bytes).
    /// </summary>
    public static ChunkSection CreateSuperflat(int y)
    {
        return new ChunkSection(
            y,
            SuperflatChunkBuilder.GetSectionBlockStates(y),
            SuperflatChunkBuilder.GetBlockLight(),
            SuperflatChunkBuilder.GetSkyLight());
    }

    public byte GetBlock(int x, int y, int z)
        => _blockStates[SuperflatChunkBuilder.BlockIndex(x, y, z)];

    public void SetBlock(int x, int y, int z, byte paletteIndex)
    {
        // Copy-on-write: create per-section array on first modification.
        if (!_dirty)
        {
            var copy = new byte[_blockStates.Length];
            _blockStates.CopyTo(copy, 0);
            _blockStates = copy;

            var blCopy = new byte[_blockLight.Length];
            _blockLight.CopyTo(blCopy, 0);
            _blockLight = blCopy;

            var slCopy = new byte[_skyLight.Length];
            _skyLight.CopyTo(slCopy, 0);
            _skyLight = slCopy;
        }

        _blockStates[SuperflatChunkBuilder.BlockIndex(x, y, z)] = paletteIndex;
        _dirty = true;
        _serialized = null; // Invalidate serialized cache
    }

    /// <summary>Serialize to 1.12.2 network format. Cached until dirtied.</summary>
    public byte[] Serialize()
    {
        if (_serialized != null && !_dirty)
            return _serialized;

        _serialized = SuperflatChunkBuilder.SerializeSection(_blockStates, _blockLight, _skyLight);
        return _serialized;
    }
}
