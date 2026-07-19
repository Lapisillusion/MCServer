namespace GameServer.World;

/// <summary>Per-player chunk subscription state, owned by the tick thread.</summary>
public sealed class PlayerChunkView
{
    private readonly HashSet<ChunkPos> _loaded = new();

    public int RequestedRadius { get; private set; } = 1;
    public int AppliedRadius { get; internal set; } = -1;
    public ChunkPos? Center { get; internal set; }
    public IReadOnlyCollection<ChunkPos> LoadedChunks => _loaded;

    public int SetRequestedRadius(int requestedRadius, int maxRadius)
    {
        if (maxRadius < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRadius));

        RequestedRadius = Math.Clamp(requestedRadius, 1, maxRadius);
        return RequestedRadius;
    }

    internal bool Add(ChunkPos pos) => _loaded.Add(pos);
    internal bool Remove(ChunkPos pos) => _loaded.Remove(pos);
    internal bool Contains(ChunkPos pos) => _loaded.Contains(pos);

    public void Clear()
    {
        _loaded.Clear();
        Center = null;
        AppliedRadius = -1;
    }
}
