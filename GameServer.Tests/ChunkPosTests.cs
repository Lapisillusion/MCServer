using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public class ChunkPosTests
{
    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(15.99, 15.99, 0, 0)]
    [InlineData(16.0, 16.0, 1, 1)]
    [InlineData(-0.01, -0.01, -1, -1)]
    [InlineData(-16.0, -16.0, -1, -1)]
    [InlineData(-16.01, -16.01, -2, -2)]
    public void FromWorldPosition_UsesFloorDivision(
        double x, double z, int expectedX, int expectedZ)
    {
        Assert.Equal(new ChunkPos(expectedX, expectedZ), ChunkPos.FromWorldPosition(x, z));
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(0.0, double.NegativeInfinity)]
    [InlineData(double.MaxValue, 0.0)]
    public void TryFromWorldPosition_RejectsUnsupportedCoordinates(double x, double z)
    {
        Assert.False(ChunkPos.TryFromWorldPosition(x, z, out _));
    }
}
