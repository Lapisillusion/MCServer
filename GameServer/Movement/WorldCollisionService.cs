using GameServer.World;

namespace GameServer.Movement;

/// <summary>Read-only collision view over loaded world chunks.</summary>
public sealed class WorldCollisionService
{
    private const int MinimumBuildHeight = 0;
    private const int MaximumBuildHeight = 255;
    private readonly ChunkProvider _chunks;

    public WorldCollisionService(ChunkProvider chunks)
    {
        _chunks = chunks;
    }

    public List<AxisAlignedBox> GetCollisionBoxes(in AxisAlignedBox query)
    {
        var result = new List<AxisAlignedBox>();
        AppendCollisionBoxes(query, result);
        return result;
    }

    public bool HasCollision(in AxisAlignedBox query)
    {
        var minX = FloorToLong(query.MinX);
        var maxX = FloorToLong(query.MaxX - AxisAlignedBox.CollisionEpsilon);
        var minY = FloorToInt(query.MinY);
        var maxY = FloorToInt(query.MaxY - AxisAlignedBox.CollisionEpsilon);
        var minZ = FloorToLong(query.MinZ);
        var maxZ = FloorToLong(query.MaxZ - AxisAlignedBox.CollisionEpsilon);

        for (var y = minY; y <= maxY; y++)
        for (var z = minZ; z <= maxZ; z++)
        for (var x = minX; x <= maxX; x++)
        {
            if (TryGetCollisionBox(x, y, z, out var box) && box.Intersects(query))
                return true;
        }

        return false;
    }

    public void AppendCollisionBoxes(in AxisAlignedBox query, List<AxisAlignedBox> destination)
    {
        var minX = FloorToLong(query.MinX);
        var maxX = FloorToLong(query.MaxX - AxisAlignedBox.CollisionEpsilon);
        var minY = FloorToInt(query.MinY);
        var maxY = FloorToInt(query.MaxY - AxisAlignedBox.CollisionEpsilon);
        var minZ = FloorToLong(query.MinZ);
        var maxZ = FloorToLong(query.MaxZ - AxisAlignedBox.CollisionEpsilon);

        for (var y = minY; y <= maxY; y++)
        for (var z = minZ; z <= maxZ; z++)
        for (var x = minX; x <= maxX; x++)
        {
            if (TryGetCollisionBox(x, y, z, out var box) && box.Intersects(query))
                destination.Add(box);
        }
    }

    private bool TryGetCollisionBox(long x, int y, long z, out AxisAlignedBox box)
    {
        if (y < MinimumBuildHeight)
        {
            box = new AxisAlignedBox(x, y, z, x + 1.0, y + 1.0, z + 1.0);
            return true;
        }

        if (y > MaximumBuildHeight)
        {
            box = default;
            return false;
        }

        if (!_chunks.TryGetBlockState(x, y, z, out var blockState))
        {
            // An unavailable chunk is a hard boundary. Never let movement escape into
            // world data the server has not loaded/generated for this player.
            box = new AxisAlignedBox(x, y, z, x + 1.0, y + 1.0, z + 1.0);
            return true;
        }

        return BlockCollisionRegistry.TryGetCollisionBox(blockState, x, y, z, out box);
    }

    private static long FloorToLong(double value)
    {
        if (!double.IsFinite(value) || value < long.MinValue || value > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        return checked((long)Math.Floor(value));
    }

    private static int FloorToInt(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var floored = Math.Floor(value);
        if (floored < int.MinValue || floored > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        return (int)floored;
    }
}
