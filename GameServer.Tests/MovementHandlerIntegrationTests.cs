using System.Net.Sockets;
using Common.MC;
using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Movement;
using GameServer.Network;
using GameServer.Players;
using GameServer.World;
using Xunit;

namespace GameServer.Tests;

public sealed class MovementHandlerIntegrationTests
{
    [Fact]
    public async Task PlayerPosition_CollisionClipsAuthoritativeStateAndQueuesOwnPlayerCorrection()
    {
        var chunks = new ChunkProvider();
        var column = chunks.GetOrGenerate(new ChunkPos(0, 0));
        column.GetOrCreateSection(0).SetBlock(1, 4, 0, SuperflatChunkBuilder.State_Dirt);
        var sessions = new SessionRegistry();
        var dispatcher = M1PlayDispatchBootstrap.Build(
            chunks, sessions, movement: new PlayerMovementService(chunks));
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var session = new SessionContext(1, socket)
        {
            State = GameSessionState.Play,
            PlayerName = "Steve",
            Player = new PlayerContext
            {
                EntityId = 1,
                PlayerName = "Steve",
                X = 0.5,
                Y = 4.0,
                Z = 0.5,
                ChunksSent = true
            }
        };
        var context = RuntimeLogContext.Empty
            .WithSessionId("1")
            .WithPlayerName("Steve")
            .WithTickId(20);

        await dispatcher.DispatchOrIgnoreAsync(
            session,
            Protocol340Ids.PlayC2S.PlayerPosition,
            context,
            BuildPlayerPositionFrame(1.5, 4.0, 0.5, true));

        Assert.Equal(0.7, session.Player.X, 10);
        Assert.True(session.Player.AwaitingTeleportConfirm);
        Assert.Equal(20, session.Player.LastClientPositionTick);
        var correction = Assert.Single(session.DrainAllOutput());
        Assert.Equal(Protocol340Ids.PlayS2C.PlayerPositionAndLook, ReadPacketId(correction));
    }

    [Fact]
    public async Task TeleportConfirm_AfterCorrectionClearsPendingStateWhenChunksAlreadySent()
    {
        var chunks = new ChunkProvider();
        chunks.GetOrGenerate(new ChunkPos(0, 0));
        var sessions = new SessionRegistry();
        var dispatcher = M1PlayDispatchBootstrap.Build(chunks, sessions);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var player = new PlayerContext
        {
            EntityId = 1,
            PlayerName = "Alex",
            X = 0.5,
            Y = 4.0,
            Z = 0.5,
            ChunksSent = true,
            AwaitingTeleportConfirm = true,
            TeleportId = 7
        };
        var session = new SessionContext(1, socket)
        {
            State = GameSessionState.Play,
            PlayerName = player.PlayerName,
            Player = player
        };

        await dispatcher.DispatchOrIgnoreAsync(
            session,
            Protocol340Ids.PlayC2S.TeleportConfirm,
            RuntimeLogContext.Empty.WithPlayerName(player.PlayerName),
            BuildTeleportConfirmFrame(player.TeleportId));

        Assert.False(player.AwaitingTeleportConfirm);
        Assert.True(player.ChunksSent);
    }

    private static byte[] BuildPlayerPositionFrame(double x, double y, double z, bool onGround)
        => McProtocolWriter.BuildMcFrame(Protocol340Ids.PlayC2S.PlayerPosition, destination =>
        {
            var offset = 0;
            offset += McProtocolWriter.WriteDouble(destination[offset..], x);
            offset += McProtocolWriter.WriteDouble(destination[offset..], y);
            offset += McProtocolWriter.WriteDouble(destination[offset..], z);
            McProtocolWriter.WriteBool(destination[offset..], onGround);
        }, payloadLength: 25);

    private static byte[] BuildTeleportConfirmFrame(int teleportId)
    {
        var payloadLength = McProtocolWriter.GetVarIntLength(teleportId);
        return McProtocolWriter.BuildMcFrame(Protocol340Ids.PlayC2S.TeleportConfirm, destination =>
        {
            McProtocolWriter.WriteVarInt(destination, teleportId);
        }, payloadLength);
    }

    private static int ReadPacketId(byte[] frame)
    {
        var offset = 0;
        Assert.True(VarIntCodec.TryRead(frame, ref offset, out _));
        Assert.True(VarIntCodec.TryRead(frame, ref offset, out var packetId));
        return packetId;
    }
}
