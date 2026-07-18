using GameServer.Inventory;
using Xunit;

namespace GameServer.Tests;

public class HotbarInventoryTests
{
    [Fact]
    public void CreateDefault_ProvidesDirtInFirstSlot()
    {
        var hotbar = HotbarInventory.CreateDefault();

        var stack = hotbar.GetSlot(0);
        Assert.NotNull(stack);
        Assert.Equal(ItemIds.Dirt, stack.ItemId);
        Assert.Equal(BlockStates.Dirt, stack.BlockState);
        Assert.Equal(64, stack.Count);
        Assert.Null(hotbar.GetSlot(1));
    }

    [Fact]
    public void TrySelect_RejectsInvalidSlotWithoutChangingSelection()
    {
        var hotbar = HotbarInventory.CreateDefault();
        Assert.True(hotbar.TrySelect(4));

        Assert.False(hotbar.TrySelect(9));
        Assert.Equal(4, hotbar.SelectedSlot);
    }

    [Fact]
    public void TryConsumeSelectedBlock_EmptiesSlotAfterLastItem()
    {
        var hotbar = HotbarInventory.CreateDefault();
        var blockState = 0;

        for (var i = 0; i < 64; i++)
            Assert.True(hotbar.TryConsumeSelectedBlock(out blockState));

        Assert.Equal(BlockStates.Dirt, blockState);
        Assert.Null(hotbar.GetSlot(0));
        Assert.False(hotbar.TryConsumeSelectedBlock(out _));
    }
}
