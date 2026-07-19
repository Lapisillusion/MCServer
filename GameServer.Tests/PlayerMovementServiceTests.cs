using GameServer.Movement;
using GameServer.Players;
using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public sealed class PlayerMovementServiceTests
{
    [Fact]
    public void MoveTo_OpenSpaceAcceptsRequestedPosition()
    {
        var chunks = CreateLoadedCenter();
        var player = CreatePlayer();
        var service = new PlayerMovementService(chunks);

        var result = service.MoveTo(player, 0.8, 4.0, 0.8, tickNumber: 10);

        Assert.False(result.WasClipped);
        Assert.Equal(0.8, player.X, 10);
        Assert.Equal(4.0, player.Y, 10);
        Assert.Equal(0.8, player.Z, 10);
        Assert.True(player.OnGround);
        Assert.Equal(10, player.LastClientPositionTick);
    }

    [Fact]
    public void MoveTo_DownwardStopsOnGround()
    {
        var chunks = CreateLoadedCenter();
        var player = CreatePlayer(y: 5.0);
        var service = new PlayerMovementService(chunks);

        var result = service.MoveTo(player, 0.5, 3.0, 0.5, tickNumber: 1);

        Assert.True(result.Resolution.CollidedVertically);
        Assert.Equal(4.0, player.Y, 10);
        Assert.True(player.OnGround);
        Assert.Equal(0.0, player.VelocityY, 10);
    }

    [Fact]
    public void MoveTo_FullBlockClipsHorizontalMovement()
    {
        var chunks = CreateLoadedCenter();
        var column = chunks.GetOrGenerate(new ChunkPos(0, 0));
        column.GetOrCreateSection(0).SetBlock(1, 4, 0, SuperflatChunkBuilder.State_Dirt);
        var player = CreatePlayer();
        var service = new PlayerMovementService(chunks);

        var result = service.MoveTo(player, 1.5, 4.0, 0.5, tickNumber: 1);

        Assert.True(result.Resolution.CollidedHorizontally);
        Assert.Equal(0.7, player.X, 10);
        Assert.Equal(4.0, player.Y, 10);
    }

    [Fact]
    public void MoveTo_UnloadedChunkActsAsHardBoundary()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        var player = CreatePlayer(x: 15.5, z: 0.5);
        var service = new PlayerMovementService(chunks);

        var result = service.MoveTo(player, 16.5, 4.0, 0.5, tickNumber: 1);

        Assert.True(result.Resolution.CollidedHorizontally);
        Assert.Equal(15.7, player.X, 10);
    }

    [Fact]
    public void MoveTo_NegativeChunkUsesCorrectLocalCoordinates()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(-1, -1));
        var player = CreatePlayer(x: -0.5, z: -0.5);
        var service = new PlayerMovementService(chunks);

        var result = service.MoveTo(player, -0.8, 4.0, -0.8, tickNumber: 2);

        Assert.False(result.WasClipped);
        Assert.Equal(-0.8, player.X, 10);
        Assert.Equal(-0.8, player.Z, 10);
        Assert.True(player.OnGround);
    }

    [Fact]
    public void NewPlayerContextPreservesSpawnCompatibleDefaults()
    {
        var player = new PlayerContext();

        Assert.Equal(0.5, player.X, 10);
        Assert.Equal(4.0, player.Y, 10);
        Assert.Equal(0.5, player.Z, 10);
        Assert.Equal(90f, player.Pitch);
    }

    [Fact]
    public void PlayerEntityStateExposesStandardBoundingBox()
    {
        var player = CreatePlayer(x: 2.0, y: 7.0, z: 3.0);

        Assert.Equal(1.7, player.BoundingBox.MinX, 10);
        Assert.Equal(7.0, player.BoundingBox.MinY, 10);
        Assert.Equal(8.8, player.BoundingBox.MaxY, 10);
        Assert.Equal(2.3, player.BoundingBox.MaxX, 10);
    }

    private static ChunkProvider CreateLoadedCenter()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        return chunks;
    }

    private static PlayerContext CreatePlayer(
        double x = 0.5,
        double y = 4.0,
        double z = 0.5)
        => new()
        {
            EntityId = 1,
            PlayerName = "Steve",
            X = x,
            Y = y,
            Z = z,
            OnGround = y <= 4.0
        };
}
