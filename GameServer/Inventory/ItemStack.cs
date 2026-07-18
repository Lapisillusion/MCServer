namespace GameServer.Inventory;

/// <summary>
/// Minimal stack used by the M6 hotbar. Only placeable block items are in scope.
/// </summary>
public sealed class ItemStack
{
    public ItemStack(int itemId, int blockState, int count)
    {
        if (itemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(itemId));
        if (blockState <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockState));
        if (count is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(count));

        ItemId = itemId;
        BlockState = blockState;
        Count = count;
    }

    public int ItemId { get; }
    public int BlockState { get; }
    public int Count { get; private set; }

    public bool TryConsumeOne()
    {
        if (Count == 0)
            return false;

        Count--;
        return true;
    }

    public ItemStackSnapshot ToSnapshot() => new(ItemId, BlockState, Count);
}

/// <summary>Serializable representation of a minimal item stack.</summary>
public readonly record struct ItemStackSnapshot(int ItemId, int BlockState, int Count);
