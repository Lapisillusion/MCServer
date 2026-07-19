using GameServer.Movement;
using Xunit;

namespace GameServer.Tests;

public sealed class AxisAlignedBoxTests
{
    [Fact]
    public void FromFeetPosition_UsesExpectedPlayerDimensions()
    {
        var box = AxisAlignedBox.FromFeetPosition(10, 4, -2, 0.6, 1.8);

        Assert.Equal(9.7, box.MinX, 10);
        Assert.Equal(4.0, box.MinY, 10);
        Assert.Equal(-2.3, box.MinZ, 10);
        Assert.Equal(10.3, box.MaxX, 10);
        Assert.Equal(5.8, box.MaxY, 10);
        Assert.Equal(-1.7, box.MaxZ, 10);
    }

    [Fact]
    public void ClipXOffset_StopsAtSolidFace()
    {
        var player = AxisAlignedBox.FromFeetPosition(0.5, 4, 0.5, 0.6, 1.8);
        var block = new AxisAlignedBox(1, 4, 0, 2, 5, 1);

        var clipped = block.ClipXOffset(player, 1.0);

        Assert.Equal(0.2, clipped, 10);
    }

    [Fact]
    public void ClipYOffset_StopsOnTopOfBlock()
    {
        var player = AxisAlignedBox.FromFeetPosition(0.5, 4.5, 0.5, 0.6, 1.8);
        var block = new AxisAlignedBox(0, 3, 0, 1, 4, 1);

        var clipped = block.ClipYOffset(player, -1.0);

        Assert.Equal(-0.5, clipped, 10);
    }
}
