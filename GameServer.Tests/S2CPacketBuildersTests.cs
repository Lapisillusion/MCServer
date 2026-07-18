using Common.MC;
using GameServer.Network;
using Xunit;

namespace GameServer.Tests;

public class S2CPacketBuildersTests
{
    [Fact]
    public void BuildJoinGame_HasCorrectLength()
    {
        var frame = S2CPacketBuilders.BuildJoinGame(
            entityId: 1, gamemode: 0, dimension: 0,
            difficulty: 2, maxPlayers: 0, levelType: "flat",
            reducedDebugInfo: false);

        Assert.NotNull(frame);
        Assert.True(frame.Length > 10);
    }

    [Fact]
    public void BuildChunkData_ProducesValidFrame()
    {
        var rawData = new byte[100];
        var frame = S2CPacketBuilders.BuildChunkData(
            chunkX: 0, chunkZ: 0,
            groundUp: true, primaryBitMask: 0x01,
            rawChunkData: rawData);

        Assert.NotNull(frame);
        Assert.True(frame.Length > rawData.Length);
    }

    [Fact]
    public void BuildChunkData_GroundUpParamAccepted()
    {
        var rawData = new byte[100];
        var frameTrue = S2CPacketBuilders.BuildChunkData(0, 0, true, 0x01, rawData);
        var frameFalse = S2CPacketBuilders.BuildChunkData(0, 0, false, 0x01, rawData);

        Assert.True(frameTrue.Length > 0);
        Assert.True(frameFalse.Length > 0);
    }

    [Fact]
    public void BuildBlockChange_CorrectEncoding()
    {
        var frame = S2CPacketBuilders.BuildBlockChange(0, 4, 0, blockState: 3);
        Assert.NotNull(frame);
        Assert.True(frame.Length >= 9);
    }

    [Fact]
    public void BuildSetCompression_Threshold256()
    {
        var frame = S2CPacketBuilders.BuildSetCompression(256);
        Assert.NotNull(frame);
        Assert.True(frame.Length > 0);

        var span = frame.AsSpan();
        var off = 0;

        while (off < span.Length && (span[off] & 0x80) != 0)
            off++;
        off++;

        var pidOff = 0;
        var pidSpan = span[off..];
        Assert.True(VarIntCodec.TryRead(pidSpan, ref pidOff, out var packetId));
        Assert.Equal(Protocol340Ids.Login.S2C_SetCompression, packetId);
    }

    [Fact]
    public void BuildKeepAlive_ProducesValidFrame()
    {
        var frame = S2CPacketBuilders.BuildKeepAlive(42);
        Assert.NotNull(frame);
        Assert.True(frame.Length > 8);
    }

    [Fact]
    public void BuildEntityTeleport_ProducesValidFrame()
    {
        var frame = S2CPacketBuilders.BuildEntityTeleport(
            entityId: 1, x: 0.5, y: 4.0, z: 0.5,
            yaw: 0f, pitch: 0f, onGround: true);
        Assert.NotNull(frame);
        Assert.True(frame.Length > 10);
    }

    [Fact]
    public void BuildAnimation_ProducesValidFrame()
    {
        var frame = S2CPacketBuilders.BuildAnimation(entityId: 1, animationType: 0);
        Assert.NotNull(frame);
        Assert.True(frame.Length > 0);
    }
}
