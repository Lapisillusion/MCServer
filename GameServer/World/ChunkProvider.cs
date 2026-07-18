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
