using GameServer.Movement;
using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public sealed class WorldCollisionServiceTests
{
    [Fact]
    public void HasCollision_DetectsSuperflatGround()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        var world = new WorldCollisionService(chunks);
        var probe = new AxisAlignedBox(0.2, 3.9, 0.2, 0.8, 4.1, 0.8);

        Assert.True(world.HasCollision(probe));
    }

    [Fact]
    public void HasCollision_ReturnsFalseForLoadedAir()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        var world = new WorldCollisionService(chunks);
        var probe = new AxisAlignedBox(0.2, 10, 0.2, 0.8, 11, 0.8);

        Assert.False(world.HasCollision(probe));
    }

    [Fact]
    public void HasCollision_TreatsMissingChunkAsSolid()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        var world = new WorldCollisionService(chunks);
        var probe = new AxisAlignedBox(16.1, 4, 0.2, 16.8, 5, 0.8);

        Assert.True(world.HasCollision(probe));
    }

    [Fact]
    public void HigherSuperflatSectionsStartAsAir()
    {
        var section = ChunkSection.CreateSuperflat(1);

        Assert.Equal(SuperflatChunkBuilder.State_Air, section.GetBlock(0, 0, 0));
        Assert.Equal(SuperflatChunkBuilder.State_Air, section.GetBlock(0, 3, 0));
    }
}
