using System.Net.Sockets;
using Common.MC;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Players;
using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public class ChunkStreamServiceTests
{
    [Fact]
    public void InitializeView_CentersOnRestoredPlayerPosition()
    {
        using var socket = CreateSocket();
        var session = CreateSession(socket, x: 160.5, z: -32.1, radius: 1);
        var service = new ChunkStreamService(new ChunkProvider());

        var update = service.InitializeView(session);

        Assert.Equal(new ChunkPos(10, -3), update.Center);
        Assert.Equal(9, update.Loaded);
        Assert.Equal(9, session.Player!.ChunkView.LoadedChunks.Count);
        Assert.Contains(new ChunkPos(10, -3), session.Player.ChunkView.LoadedChunks);
        Assert.Equal(9, session.DrainAllOutput().Count(PacketIdIsChunkData));
    }

    [Fact]
    public void UpdateView_CrossingOneChunkLoadsAndUnloadsLeadingEdges()
    {
        using var socket = CreateSocket();
        var session = CreateSession(socket, x: 0.5, z: 0.5, radius: 1);
        var service = new ChunkStreamService(new ChunkProvider());
        service.InitializeView(session);
        session.DrainAllOutput();

        session.Player!.X = 16.5;
        var update = service.UpdateView(session);
        var frames = session.DrainAllOutput();

        Assert.Equal(new ChunkPos(1, 0), update.Center);
        Assert.Equal(3, update.Loaded);
        Assert.Equal(3, update.Unloaded);
        Assert.Equal(9, session.Player.ChunkView.LoadedChunks.Count);
        Assert.Equal(3, frames.Count(PacketIdIsChunkData));
        Assert.Equal(3, frames.Count(PacketIdIsUnloadChunk));
    }

    [Fact]
    public void UpdateView_WithinSameChunkProducesNoFrames()
    {
        using var socket = CreateSocket();
        var session = CreateSession(socket, x: 0.5, z: 0.5, radius: 1);
        var service = new ChunkStreamService(new ChunkProvider());
        service.InitializeView(session);
        session.DrainAllOutput();

        session.Player!.X = 15.9;
        session.Player.Z = 15.9;
        var update = service.UpdateView(session);

        Assert.True(update.IsEmpty);
        Assert.Empty(session.DrainAllOutput());
    }

    [Fact]
    public void UpdateView_RadiusChangeReconcilesWithoutCenterMovement()
    {
        using var socket = CreateSocket();
        var session = CreateSession(socket, x: 0.5, z: 0.5, radius: 1);
        var service = new ChunkStreamService(new ChunkProvider());
        service.InitializeView(session);
        session.DrainAllOutput();

        session.Player!.ChunkView.SetRequestedRadius(2, maxRadius: 4);
        var update = service.UpdateView(session);

        Assert.Equal(16, update.Loaded);
        Assert.Equal(0, update.Unloaded);
        Assert.Equal(25, session.Player.ChunkView.LoadedChunks.Count);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(3, 3)]
    [InlineData(99, 4)]
    public void PlayerChunkView_ClampsRequestedRadius(int requested, int expected)
    {
        var view = new PlayerChunkView();
        Assert.Equal(expected, view.SetRequestedRadius(requested, maxRadius: 4));
    }

    private static SessionContext CreateSession(Socket socket, double x, double z, int radius)
    {
        var player = new PlayerManager().CreatePlayer(Guid.NewGuid().ToString("N"));
        player.X = x;
        player.Z = z;
        player.ChunkView.SetRequestedRadius(radius, maxRadius: 4);
        return new SessionContext(1, socket)
        {
            State = GameSessionState.Play,
            Player = player
        };
    }

    private static Socket CreateSocket()
        => new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    private static bool PacketIdIsChunkData(byte[] frame)
        => ReadPacketId(frame) == Protocol340Ids.PlayS2C.ChunkData;

    private static bool PacketIdIsUnloadChunk(byte[] frame)
        => ReadPacketId(frame) == Protocol340Ids.PlayS2C.UnloadChunk;

    private static int ReadPacketId(byte[] frame)
    {
        var off = 0;
        Assert.True(VarIntCodec.TryRead(frame, ref off, out _));
        Assert.True(VarIntCodec.TryRead(frame, ref off, out var packetId));
        return packetId;
    }
}
