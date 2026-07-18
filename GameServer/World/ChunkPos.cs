namespace GameServer.World;

/// <summary>
/// Compact chunk coordinate. A struct to avoid heap allocation.
/// Key for chunk storage: ((long)X << 32) | (uint)Z.
/// </summary>
public readonly record struct ChunkPos(int X, int Z)
{
    public long ToLong() => ((long)X << 32) | (uint)Z;

    public override string ToString() => $"({X}, {Z})";
}
