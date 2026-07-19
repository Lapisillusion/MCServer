namespace GameServer.World;

/// <summary>
/// Compact chunk coordinate. A struct to avoid heap allocation.
/// Key for chunk storage: ((long)X &lt;&lt; 32) | (uint)Z.
/// </summary>
public readonly record struct ChunkPos(int X, int Z)
{
    public static ChunkPos FromWorldPosition(double x, double z)
    {
        if (!TryFromWorldPosition(x, z, out var pos))
            throw new ArgumentOutOfRangeException(nameof(x), "World coordinates are outside the supported chunk range.");
        return pos;
    }

    public static bool TryFromWorldPosition(double x, double z, out ChunkPos pos)
    {
        pos = default;
        if (!double.IsFinite(x) || !double.IsFinite(z))
            return false;

        var chunkX = Math.Floor(x / 16.0);
        var chunkZ = Math.Floor(z / 16.0);
        if (chunkX < int.MinValue || chunkX > int.MaxValue ||
            chunkZ < int.MinValue || chunkZ > int.MaxValue)
            return false;

        pos = new ChunkPos((int)chunkX, (int)chunkZ);
        return true;
    }

    public long ToLong() => ((long)X << 32) | (uint)Z;

    public override string ToString() => $"({X}, {Z})";
}
