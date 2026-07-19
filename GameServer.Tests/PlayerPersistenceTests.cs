using GameServer.Dimension;
using GameServer.Inventory;
using GameServer.Persistence;
using GameServer.Players;
using Xunit;

namespace GameServer.Tests;

public class PlayerPersistenceTests
{
    [Fact]
    public async Task FileStore_RoundTripsPositionAndHotbar()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new FilePlayerDataStore(root);
            var source = new PlayerManager().CreatePlayer("player-1");
            source.Dimension = 0;
            source.X = 12.5;
            source.Y = 65;
            source.Z = -8.25;
            source.Yaw = 90f;
            source.Pitch = -15f;
            source.OnGround = true;
            Assert.True(source.Hotbar.TryConsumeSelectedBlock(out _));
            Assert.True(source.Hotbar.TryConsumeSelectedBlock(out _));

            await store.SaveAsync(PlayerStatePersistence.Capture(source));
            var saved = await store.LoadAsync("player-1");

            Assert.NotNull(saved);
            var restored = new PlayerManager().CreatePlayer("player-1");
            var dimensions = CreateDimensions();
            Assert.True(PlayerStatePersistence.TryApply(restored, saved!, dimensions, out var error), error);
            Assert.Equal(12.5, restored.X);
            Assert.Equal(65, restored.Y);
            Assert.Equal(-8.25, restored.Z);
            Assert.Equal(90f, restored.Yaw);
            Assert.Equal(-15f, restored.Pitch);
            Assert.Equal(62, restored.Hotbar.GetSlot(0)!.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileStore_CorruptJson_ReturnsNoState()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new FilePlayerDataStore(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(store.GetFilePath("player-1"), "not json");

            Assert.Null(await store.LoadAsync("player-1"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryApply_InvalidStateLeavesSpawnDefaultsUntouched()
    {
        var player = new PlayerManager().CreatePlayer("player-1");
        var saved = new PlayerSaveData(
            "player-1", 99, double.NaN, 4, 0.5, 0, 0, true, 9,
            new ItemStackSnapshot?[HotbarInventory.SlotCount]);

        Assert.False(PlayerStatePersistence.TryApply(player, saved, CreateDimensions(), out _));
        Assert.Equal(0.5, player.X);
        Assert.Equal(4.0, player.Y);
        Assert.Equal(64, player.Hotbar.GetSlot(0)!.Count);
    }

    [Fact]
    public void TryApply_UnstreamableCoordinatesLeaveSpawnDefaultsUntouched()
    {
        var player = new PlayerManager().CreatePlayer("player-1");
        var slots = player.Hotbar.Snapshot();
        var saved = new PlayerSaveData(
            "player-1", 0, double.MaxValue, 4, 0.5, 0, 0, true, 0, slots);

        Assert.False(PlayerStatePersistence.TryApply(player, saved, CreateDimensions(), out _));
        Assert.Equal(0.5, player.X);
        Assert.Equal(0.5, player.Z);
    }

    [Fact]
    public void HotbarRestore_RejectsInvalidStackWithoutChangingExistingItems()
    {
        var hotbar = HotbarInventory.CreateDefault();
        var invalid = new ItemStackSnapshot?[HotbarInventory.SlotCount];
        invalid[0] = new ItemStackSnapshot(ItemIds.Dirt, BlockStates.Dirt, 65);

        Assert.False(hotbar.TryRestore(0, invalid));
        Assert.Equal(64, hotbar.GetSlot(0)!.Count);
    }

    private static DimensionManager CreateDimensions()
    {
        var dimensions = new DimensionManager();
        dimensions.RegisterDefaults();
        return dimensions;
    }

    private static string CreateTempDirectory()
        => Path.Combine(Path.GetTempPath(), "MCServer.Tests", Guid.NewGuid().ToString("N"));
}
