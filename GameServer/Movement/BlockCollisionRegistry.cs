namespace GameServer.Movement;

/// <summary>
/// Maps protocol-340 block states to collision geometry. The current world only exposes
/// air and full-cube blocks; special shapes can be added here without changing movement code.
/// </summary>
public static class BlockCollisionRegistry
{
    public static bool IsCollidable(int blockState) => blockState != 0;

    public static bool TryGetCollisionBox(
        int blockState, long blockX, int blockY, long blockZ,
        out AxisAlignedBox collisionBox)
    {
        if (!IsCollidable(blockState))
        {
            collisionBox = default;
            return false;
        }

        collisionBox = new AxisAlignedBox(
            blockX, blockY, blockZ,
            blockX + 1.0, blockY + 1.0, blockZ + 1.0);
        return true;
    }
}
