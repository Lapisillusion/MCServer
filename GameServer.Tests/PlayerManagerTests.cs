using GameServer.Players;
using Xunit;

namespace GameServer.Tests;

public class PlayerManagerTests
{
    [Fact]
    public void CreatePlayer_AssignsUniqueEntityId()
    {
        var manager = new PlayerManager();
        var p1 = manager.CreatePlayer("uuid-1");
        var p2 = manager.CreatePlayer("uuid-2");
        Assert.NotEqual(p1.EntityId, p2.EntityId);
    }

    [Fact]
    public void CreatePlayer_EntityIdStartsFromOne()
    {
        var manager = new PlayerManager();
        var player = manager.CreatePlayer("uuid-1");
        Assert.Equal(1, player.EntityId);
    }

    [Fact]
    public void CreatePlayer_SetsDefaultPosition()
    {
        var manager = new PlayerManager();
        var player = manager.CreatePlayer("uuid-1");
        Assert.Equal(0.5, player.X);
        Assert.Equal(4.0, player.Y);
        Assert.Equal(0.5, player.Z);
    }

    [Fact]
    public void TryGetPlayer_Exists_ReturnsTrue()
    {
        var manager = new PlayerManager();
        var created = manager.CreatePlayer("uuid-1");
        var found = manager.TryGetPlayer(created.EntityId, out var player);
        Assert.True(found);
        Assert.NotNull(player);
        Assert.Equal("uuid-1", player.PlayerId);
    }

    [Fact]
    public void TryGetPlayer_NotExists_ReturnsFalse()
    {
        var manager = new PlayerManager();
        var found = manager.TryGetPlayer(999, out var player);
        Assert.False(found);
        Assert.Null(player);
    }

    [Fact]
    public void RemovePlayer_Exists_ReturnsTrue()
    {
        var manager = new PlayerManager();
        var created = manager.CreatePlayer("uuid-1");
        var removed = manager.RemovePlayer(created.EntityId, out var player);
        Assert.True(removed);
        Assert.NotNull(player);

        var found = manager.TryGetPlayer(created.EntityId, out _);
        Assert.False(found);
    }

    [Fact]
    public void RemovePlayer_NotExists_ReturnsFalse()
    {
        var manager = new PlayerManager();
        var removed = manager.RemovePlayer(999, out var player);
        Assert.False(removed);
        Assert.Null(player);
    }

    [Fact]
    public void CreatePlayer_IncrementsCount()
    {
        var manager = new PlayerManager();
        Assert.Equal(0, manager.Count);
        manager.CreatePlayer("uuid-1");
        Assert.Equal(1, manager.Count);
        manager.CreatePlayer("uuid-2");
        Assert.Equal(2, manager.Count);
    }
}
