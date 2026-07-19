using System.Net.Sockets;
using Common.MC;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Movement;
using GameServer.Players;
using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public sealed class PlayerPhysicsServiceTests
{
    [Fact]
    public void Update_AirbornePlayerWithoutClientPositionAppliesGravityAndCorrection()
    {
        var chunks = CreateLoadedCenter();
        var movement = new PlayerMovementService(chunks);
        var physics = new PlayerPhysicsService(movement);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreateSession(socket, y: 10.0);

        physics.Update(session, tickNumber: 5);

        Assert.True(session.Player!.Y < 10.0);
        Assert.True(session.Player.VelocityY < 0);
        Assert.True(session.Player.AwaitingTeleportConfirm);
        var correction = Assert.Single(session.DrainAllOutput());
        Assert.Equal(Protocol340Ids.PlayS2C.PlayerPositionAndLook, ReadPacketId(correction));
    }

    [Fact]
    public void Update_CurrentTickClientPositionDoesNotDoubleIntegrateGravity()
    {
        var chunks = CreateLoadedCenter();
        var movement = new PlayerMovementService(chunks);
        var physics = new PlayerPhysicsService(movement);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreateSession(socket, y: 10.0);
        session.Player!.LastClientPositionTick = 5;

        physics.Update(session, tickNumber: 5);

        Assert.Equal(10.0, session.Player.Y, 10);
        Assert.Empty(session.DrainAllOutput());
    }

    [Fact]
    public void Update_AwaitingTeleportConfirmDoesNotAdvanceServerPosition()
    {
        var chunks = CreateLoadedCenter();
        var movement = new PlayerMovementService(chunks);
        var physics = new PlayerPhysicsService(movement);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreateSession(socket, y: 10.0);
        session.Player!.AwaitingTeleportConfirm = true;
        session.Player.VelocityY = -0.5;

        physics.Update(session, tickNumber: 6);

        Assert.Equal(10.0, session.Player.Y, 10);
        Assert.Empty(session.DrainAllOutput());
    }

    [Fact]
    public void Update_FallingPlayerLandsOnGroundAndClearsVerticalVelocity()
    {
        var chunks = CreateLoadedCenter();
        var movement = new PlayerMovementService(chunks);
        var physics = new PlayerPhysicsService(movement);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = CreateSession(socket, y: 4.05);
        session.Player!.VelocityY = -0.5;

        physics.Update(session, tickNumber: 1);

        Assert.Equal(4.0, session.Player.Y, 10);
        Assert.True(session.Player.OnGround);
        Assert.Equal(0.0, session.Player.VelocityY, 10);
    }

    private static SessionContext CreateSession(Socket socket, double y)
    {
        var player = new PlayerContext
        {
            EntityId = 1,
            PlayerName = "Alex",
            X = 0.5,
            Y = y,
            Z = 0.5,
            ChunksSent = true,
            AwaitingTeleportConfirm = false,
            OnGround = false
        };
        return new SessionContext(1, socket)
        {
            State = GameSessionState.Play,
            PlayerName = player.PlayerName,
            Player = player
        };
    }

    private static ChunkProvider CreateLoadedCenter()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        return chunks;
    }

    private static int ReadPacketId(byte[] frame)
    {
        var offset = 0;
        Assert.True(VarIntCodec.TryRead(frame, ref offset, out _));
        Assert.True(VarIntCodec.TryRead(frame, ref offset, out var packetId));
        return packetId;
    }
}
