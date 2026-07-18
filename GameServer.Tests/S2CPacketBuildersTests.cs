using Common.MC;
using GameServer.Inventory;
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

    [Fact]
    public void BuildSetSlot_UsesSetSlotPacketAndEncodesStack()
    {
        var frame = S2CPacketBuilders.BuildSetSlot(0, 36, new ItemStack(ItemIds.Dirt, BlockStates.Dirt, 64));
        var off = 0;
        Assert.True(VarIntCodec.TryRead(frame, ref off, out _));
        Assert.True(VarIntCodec.TryRead(frame, ref off, out var packetId));

        Assert.Equal(Protocol340Ids.PlayS2C.SetSlot, packetId);
        Assert.Equal(0, frame[off]);
        Assert.Equal(0, frame[off + 1]);
        Assert.Equal(36, frame[off + 2]);
        Assert.Equal(0, frame[off + 3]);
        Assert.Equal(ItemIds.Dirt, frame[off + 4]);
        Assert.Equal(64, frame[off + 5]);
        Assert.Equal(0, frame[off + 6]);
        Assert.Equal(0, frame[off + 7]);
        Assert.Equal(0, frame[off + 8]);
    }

    [Fact]
    public void BuildSetSlot_EmptyStackUsesLegacyMinusOneItemId()
    {
        var frame = S2CPacketBuilders.BuildSetSlot(0, 37, null);
        var off = 0;
        Assert.True(VarIntCodec.TryRead(frame, ref off, out _));
        Assert.True(VarIntCodec.TryRead(frame, ref off, out _));

        Assert.Equal(0, frame[off]);
        Assert.Equal(0, frame[off + 1]);
        Assert.Equal(37, frame[off + 2]);
        Assert.Equal(0xFF, frame[off + 3]);
        Assert.Equal(0xFF, frame[off + 4]);
    }
}
