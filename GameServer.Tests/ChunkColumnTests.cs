using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public class ChunkColumnTests
{
    [Fact]
    public void BuildChunkData_CachesResult()
    {
        var pos = new ChunkPos(0, 0);
        var biomes = new byte[256];
        var column = new ChunkColumn(pos, biomes);
        column.SetSection(ChunkSection.CreateSuperflat(0));

        var data1 = column.BuildChunkData(primaryBitMask: 0x01);
        var data2 = column.BuildChunkData(primaryBitMask: 0x01);

        Assert.Same(data1, data2);
    }

    [Fact]
    public void BuildChunkData_DifferentBitMask_ReturnsNewResult()
    {
        var pos = new ChunkPos(0, 0);
        var biomes = new byte[256];
        var column = new ChunkColumn(pos, biomes);
        column.SetSection(ChunkSection.CreateSuperflat(0));

        var data1 = column.BuildChunkData(primaryBitMask: 0x01);
        var data2 = column.BuildChunkData(primaryBitMask: 0x02);

        Assert.NotSame(data1, data2);
        Assert.True(data2.Length < data1.Length);
    }

    [Fact]
    public void BuildChunkData_GroundUpTrue_IncludesBiomes()
    {
        var pos = new ChunkPos(0, 0);
        var biomes = new byte[256];
        var column = new ChunkColumn(pos, biomes) { GroundUpContinuous = true };
        column.SetSection(ChunkSection.CreateSuperflat(0));

        var data = column.BuildChunkData(primaryBitMask: 0x01);

        Assert.Contains(biomes[0], data);
    }

    [Fact]
    public void BuildChunkData_GroundUpFalse_ExcludesBiomes()
    {
        var pos = new ChunkPos(0, 0);
        var biomes = new byte[256];
        var column = new ChunkColumn(pos, biomes) { GroundUpContinuous = false };
        column.SetSection(ChunkSection.CreateSuperflat(0));

        var dataFalse = column.BuildChunkData(primaryBitMask: 0x01);

        var column2 = new ChunkColumn(pos, biomes) { GroundUpContinuous = true };
        column2.SetSection(ChunkSection.CreateSuperflat(0));
        var dataTrue = column2.BuildChunkData(primaryBitMask: 0x01);

        Assert.True(dataFalse.Length < dataTrue.Length);
    }

    [Fact]
    public void InvalidateChunkCache_CausesRebuild()
    {
        var pos = new ChunkPos(0, 0);
        var biomes = new byte[256];
        var column = new ChunkColumn(pos, biomes);
        column.SetSection(ChunkSection.CreateSuperflat(0));

        var data1 = column.BuildChunkData(primaryBitMask: 0x01);
        column.InvalidateChunkCache();
        var data2 = column.BuildChunkData(primaryBitMask: 0x01);

        Assert.NotSame(data1, data2);
        Assert.Equal(data1.Length, data2.Length);
    }

    [Fact]
    public void SetSection_OutOfRange_Throws()
    {
        var pos = new ChunkPos(0, 0);
        var biomes = new byte[256];
        var column = new ChunkColumn(pos, biomes);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            column.SetSection(new ChunkSection(16, new byte[4096], new byte[2048], new byte[2048])));
    }
}
