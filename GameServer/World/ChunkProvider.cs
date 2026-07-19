using System.Collections.Concurrent;

namespace GameServer.World;

/// <summary>
/// Owns chunk column storage. Generates superflat chunks on first access,
/// returns cached columns on subsequent access.
///
/// Uses ConcurrentDictionary for lock-free reads and fine-grained writes.
/// </summary>
public sealed class ChunkProvider
{
    private readonly ConcurrentDictionary<long, ChunkColumn> _columns = new();

    public int Count => _columns.Count;

    public ChunkColumn GetOrGenerate(ChunkPos pos)
    {
        var key = pos.ToLong();
        if (_columns.TryGetValue(key, out var existing))
            return existing;

        var column = ChunkColumn.CreateSuperflat(pos);
        var actual = _columns.GetOrAdd(key, column);
        return actual;
    }

    /// <summary>Lookup without generation.</summary>
    public bool TryGetColumn(ChunkPos pos, out ChunkColumn column)
        => _columns.TryGetValue(pos.ToLong(), out column!);

    /// <summary>
    /// Reads a world block state without generating missing chunks. Returns false only when
    /// the target chunk is unavailable or the coordinate cannot map to a protocol chunk.
    /// </summary>
    public bool TryGetBlockState(long worldX, int worldY, long worldZ, out int blockState)
    {
        blockState = SuperflatChunkBuilder.State_Air;
        if (worldY is < 0 or > 255)
            return true;

        var chunkXLong = worldX >> 4;
        var chunkZLong = worldZ >> 4;
        if (chunkXLong is < int.MinValue or > int.MaxValue ||
            chunkZLong is < int.MinValue or > int.MaxValue)
            return false;

        if (!TryGetColumn(new ChunkPos((int)chunkXLong, (int)chunkZLong), out var column))
            return false;

        var section = column.GetSection(worldY >> 4);
        if (section == null)
            return true;

        blockState = section.GetBlock(
            (int)(worldX & 0xF),
            worldY & 0xF,
            (int)(worldZ & 0xF));
        return true;
    }

    /// <summary>
    /// Generate a (2*radius+1) × (2*radius+1) grid centered on (cx, cz).
    /// Each column is independently generated/cached.
    /// </summary>
    public IReadOnlyList<ChunkColumn> GetOrGenerateSpawnGrid(int cx, int cz, int radius)
    {
        var count = (2 * radius + 1) * (2 * radius + 1);
        var result = new List<ChunkColumn>(count);

        for (var dx = -radius; dx <= radius; dx++)
        for (var dz = -radius; dz <= radius; dz++)
        {
            result.Add(GetOrGenerate(new ChunkPos(cx + dx, cz + dz)));
        }

        return result;
    }
}
