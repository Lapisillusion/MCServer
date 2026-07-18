using GameServer.Interactions;
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
}
