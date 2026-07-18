namespace GameServer.Interactions;

/// <summary>
/// Pure block manipulation logic. No socket access — returns S2C frame bytes
/// for the caller to enqueue as output.
/// </summary>
public static class BlockInteractionService
{
    /// <summary>
    /// Decode 1.12.2 packed position (64-bit: 26-bit X, 12-bit Y, 26-bit Z).
    /// </summary>
    public static (int X, int Y, int Z) DecodePosition(long encoded)
    {
        var x = (int)(encoded >> 38);
        var y = (int)((encoded >> 26) & 0xFFF);
        var z = (int)(encoded << 38 >> 38); // sign-extend
        return (x, y, z);
    }

    /// <summary>
    /// Encode block position back to packed 64-bit long.
    /// </summary>
    public static long EncodePosition(int x, int y, int z)
    {
        return ((long)(x & 0x3FFFFFF) << 38) | ((long)(y & 0xFFF) << 26) | (long)(z & 0x3FFFFFF);
    }

    /// <summary>
    /// Process a completed dig (status=2). Returns a BlockChange S2C frame, or null.
    /// </summary>
    public static byte[]? ProcessDig(
        World.ChunkProvider chunkProvider,
        long encodedPosition,
        byte status,
        out string? error)
    {
        error = null;

        // Only process completed digs (status 0=started, 1=cancelled, 2=finished)
        if (status != 2)
            return null;

        var (x, y, z) = DecodePosition(encodedPosition);

        var chunkX = x >> 4;
        var chunkZ = z >> 4;
        if (!chunkProvider.TryGetColumn(new World.ChunkPos(chunkX, chunkZ), out var column))
        {
            error = $"Chunk not loaded: ({chunkX}, {chunkZ})";
            return null;
        }

        var sectionIdx = y >> 4;
        var section = column.GetOrCreateSection(sectionIdx);

        var localX = x & 0xF;
        var localY = y & 0xF;
        var localZ = z & 0xF;

        section.SetBlock(localX, localY, localZ, paletteIndex: 0); // 0 = Air
        column.InvalidateChunkCache();

        return Network.S2CPacketBuilders.BuildBlockChange(x, y, z, blockState: 0);
    }

    /// <summary>
    /// Process a block placement. Returns a BlockChange S2C frame, or null.
    /// </summary>
    public static byte[]? ProcessPlace(
        World.ChunkProvider chunkProvider,
        long encodedPosition,
        int face,
        int blockState,
        out string? error)
    {
        error = null;

        var (x, y, z) = DecodePosition(encodedPosition);

        // Calculate target position (adjacent to the clicked face)
        var (tx, ty, tz) = GetAdjacentPosition(x, y, z, face);

        var chunkX = tx >> 4;
        var chunkZ = tz >> 4;
        var column = chunkProvider.GetOrGenerate(new World.ChunkPos(chunkX, chunkZ));

        var sectionIdx = ty >> 4;
        if (sectionIdx is < 0 or > 15)
        {
            error = $"Y out of bounds: {ty}";
            return null;
        }

        var section = column.GetOrCreateSection(sectionIdx);

        var localX = tx & 0xF;
        var localY = ty & 0xF;
        var localZ = tz & 0xF;

        // Check if target is already occupied
        var existing = section.GetBlock(localX, localY, localZ);
        if (existing != 0) // not air
        {
            error = "Target block is already occupied";
            return null;
        }

        section.SetBlock(localX, localY, localZ, (byte)blockState);
        column.InvalidateChunkCache();

        return Network.S2CPacketBuilders.BuildBlockChange(tx, ty, tz, blockState);
    }

    /// <summary>Get block position adjacent to (x,y,z) on the specified face.</summary>
    private static (int X, int Y, int Z) GetAdjacentPosition(int x, int y, int z, int face)
    {
        return face switch
        {
            0 => (x, y - 1, z), // bottom
            1 => (x, y + 1, z), // top
            2 => (x, y, z - 1), // north
            3 => (x, y, z + 1), // south
            4 => (x - 1, y, z), // west
            5 => (x + 1, y, z), // east
            _ => (x, y, z)
        };
    }
}
