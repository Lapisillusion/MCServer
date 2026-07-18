namespace GameServer.Inventory;

/// <summary>
/// Server-authoritative nine-slot hotbar. Full inventories and containers are out of scope.
/// </summary>
public sealed class HotbarInventory
{
    public const int SlotCount = 9;

    private readonly ItemStack?[] _slots = new ItemStack?[SlotCount];

    public int SelectedSlot { get; private set; }

    public static HotbarInventory CreateDefault()
    {
        var hotbar = new HotbarInventory();
        hotbar._slots[0] = new ItemStack(ItemIds.Dirt, BlockStates.Dirt, count: 64);
        return hotbar;
    }

    public ItemStack? GetSlot(int slot)
        => (uint)slot < SlotCount ? _slots[slot] : throw new ArgumentOutOfRangeException(nameof(slot));

    public bool TrySelect(int slot)
    {
        if ((uint)slot >= SlotCount)
            return false;

        SelectedSlot = slot;
        return true;
    }

    /// <summary>Consumes one item from the selected slot and returns its block state.</summary>
    public bool TryConsumeSelectedBlock(out int blockState)
    {
        blockState = 0;
        var stack = _slots[SelectedSlot];
        if (stack == null || !stack.TryConsumeOne())
            return false;

        blockState = stack.BlockState;
        if (stack.Count == 0)
            _slots[SelectedSlot] = null;
        return true;
    }
}

/// <summary>Only IDs required by the M6 starter hotbar.</summary>
public static class ItemIds
{
    public const int Dirt = 3;
}

/// <summary>Protocol-340 block state IDs required by the M6 starter hotbar.</summary>
public static class BlockStates
{
    public const int Dirt = 48;
}
