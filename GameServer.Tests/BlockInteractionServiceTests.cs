using GameServer.Interactions;
using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public class BlockInteractionServiceTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(100, 64, -100)]
    [InlineData(-100, 0, 100)]
    [InlineData(int.MaxValue >> 6, 255, int.MinValue >> 6)]
    public void DecodeEncodePosition_RoundTrip(int x, int y, int z)
    {
        var encoded = BlockInteractionService.EncodePosition(x, y, z);
        var (dx, dy, dz) = BlockInteractionService.DecodePosition(encoded);
        Assert.Equal(x, dx);
        Assert.Equal(y, dy);
        Assert.Equal(z, dz);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, -1, 0)]
    [InlineData(1, 0, 0, 0, 0, 1, 0)]
    [InlineData(2, 0, 0, 0, 0, 0, -1)]
    [InlineData(3, 0, 0, 0, 0, 0, 1)]
    [InlineData(4, 0, 0, 0, -1, 0, 0)]
    [InlineData(5, 0, 0, 0, 1, 0, 0)]
    public void AdjacentPosition_Face(int face, int fromX, int fromY, int fromZ,
        int expectedX, int expectedY, int expectedZ)
    {
        var pos = BlockInteractionService.EncodePosition(fromX, fromY, fromZ);
        var (x, y, z) = BlockInteractionService.DecodePosition(pos);
        var faceResults = new (int X, int Y, int Z)[]
        {
            (x, y - 1, z),
            (x, y + 1, z),
            (x, y, z - 1),
            (x, y, z + 1),
            (x - 1, y, z),
            (x + 1, y, z),
        };
        var result = faceResults[face];
        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedZ, result.Z);
    }

    [Fact]
    public void ProcessPlace_InvalidFace_DoesNotCreateOrModifyChunk()
    {
        var provider = new ChunkProvider();
        var position = BlockInteractionService.EncodePosition(0, 4, 0);

        var frame = BlockInteractionService.ProcessPlace(provider, position, face: 6, blockState: 48, out var error);

        Assert.Null(frame);
        Assert.Equal("Invalid block face: 6", error);
        Assert.False(provider.TryGetColumn(new ChunkPos(0, 0), out _));
    }

    [Fact]
    public void IsWithinReach_RejectsDistantTarget()
    {
        var near = BlockInteractionService.EncodePosition(0, 4, 0);
        var distant = BlockInteractionService.EncodePosition(20, 4, 0);

        Assert.True(BlockInteractionService.IsWithinReach(0.5, 4, 0.5, near));
        Assert.False(BlockInteractionService.IsWithinReach(0.5, 4, 0.5, distant));
    }
}
