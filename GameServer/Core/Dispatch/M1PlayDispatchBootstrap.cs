using System.Buffers.Binary;
using System.Text;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Network;
using GameServer.Network.Backend;
using GameServer.World;

namespace GameServer.Core.Dispatch;

public static class M1PlayDispatchBootstrap
{
    // Minecraft Java 1.12.2 (protocol 340), serverbound Play IDs.
    public const int C2S_TeleportConfirm = 0x00;
    public const int C2S_ClientSettings = 0x04;
    public const int C2S_KeepAlive = 0x0B;
    public const int C2S_Player = 0x0C;
    public const int C2S_PlayerPosition = 0x0D;
    public const int C2S_PlayerPositionAndLook = 0x0E;
    public const int C2S_PlayerLook = 0x0F;
    public const int C2S_HeldItemChange = 0x1A;

    private static readonly int[] SpawnChunkOffsets =
    {
        -1, -1, -1, 0, -1, 1,
        0, -1, 0, 0, 0, 1,
        1, -1, 1, 0, 1, 1
    };

    public static PlayPacketDispatcher Build()
    {
        var dispatcher = new PlayPacketDispatcher();

        dispatcher.Register(GameSessionState.New, C2S_ClientSettings, HandleClientSettings);

        dispatcher.Register(GameSessionState.Play, C2S_TeleportConfirm, HandleTeleportConfirm);
        dispatcher.Register(GameSessionState.Play, C2S_ClientSettings, HandleClientSettings);
        dispatcher.Register(GameSessionState.Play, C2S_KeepAlive, HandleKeepAlive);
        dispatcher.Register(GameSessionState.Play, C2S_Player, HandlePlayer);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerPosition, HandlePlayerPosition);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerPositionAndLook, HandlePlayerPositionAndLook);
        dispatcher.Register(GameSessionState.Play, C2S_PlayerLook, HandlePlayerLook);
        dispatcher.Register(GameSessionState.Play, C2S_HeldItemChange, HandleHeldItemChange);

        return dispatcher;
    }

    // ---- C2S frame parsing helpers ----

    /// <summary>Skips the frame header (two VarInts: length prefix + packetId). Returns the C2S payload.</summary>
    private static bool SkipFrameHeader(ReadOnlyMemory<byte> frame, out int payloadOffset)
    {
        var span = frame.Span;
        var off = 0;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var payloadLen))
        {
            payloadOffset = 0;
            return false;
        }

        if (payloadLen < 0 || payloadLen > McPlayFrameCodec.MaxFramePayload)
        {
            payloadOffset = 0;
            return false;
        }

        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out _))
        {
            payloadOffset = 0;
            return false;
        }

        payloadOffset = off;
        return true;
    }

    // ---- Handlers ----

    private static ValueTask HandleTeleportConfirm(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var teleportId))
            return ValueTask.CompletedTask;

        Console.WriteLine($"[M1] TeleportConfirm: sessionId={session.SessionId}, teleportId={teleportId}");

        if (session.Player == null || session.Player.ChunksSent)
            return ValueTask.CompletedTask;

        session.Player.ChunksSent = true;

        // Send a 3x3 grid of superflat chunks centered on (0, 0)
        for (var i = 0; i < SpawnChunkOffsets.Length; i += 2)
        {
            var cx = SpawnChunkOffsets[i];
            var cz = SpawnChunkOffsets[i + 1];
            var chunkPacket = SuperflatChunkBuilder.BuildChunkPacket(cx, cz);
            session.Stream?.Write(chunkPacket);
        }

        session.Stream?.Flush();
        Console.WriteLine($"[M1] Chunks sent: sessionId={session.SessionId}, grid=3x3 superflat");
        return ValueTask.CompletedTask;
    }

    private static ValueTask HandleClientSettings(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;

        // Locale (string)
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var localeLen))
            return ValueTask.CompletedTask;
        if (localeLen < 0 || localeLen > 64 || off + localeLen > span.Length)
            return ValueTask.CompletedTask;

        var locale = Encoding.UTF8.GetString(span.Slice(off, localeLen));
        off += localeLen;

        // View Distance (i8)
        if (off >= span.Length) return ValueTask.CompletedTask;
        var viewDistance = (sbyte)span[off++];

        // Chat Flags (VarInt)
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var chatFlags))
            return ValueTask.CompletedTask;

        // Chat Colors (bool)
        if (off >= span.Length) return ValueTask.CompletedTask;
        var chatColors = span[off++] != 0;

        // Skin Parts (u8)
        if (off >= span.Length) return ValueTask.CompletedTask;
        var skinParts = span[off++];

        // Main Hand (VarInt)
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var mainHand))
            return ValueTask.CompletedTask;

        Console.WriteLine($"[M1] ClientSettings: sessionId={session.SessionId}, locale={locale}, viewDistance={viewDistance}");
        return ValueTask.CompletedTask;
    }

    private static ValueTask HandleKeepAlive(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 8 > span.Length)
            return ValueTask.CompletedTask;

        var keepAliveId = BinaryPrimitives.ReadInt64BigEndian(span[off..]);
        Console.WriteLine($"[M1] KeepAlive: sessionId={session.SessionId}, id={keepAliveId}");

        var response = S2CPacketBuilders.BuildKeepAlive(keepAliveId);
        session.Stream?.Write(response);
        session.Stream?.Flush();
        return ValueTask.CompletedTask;
    }

    private static ValueTask HandlePlayer(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off >= span.Length)
            return ValueTask.CompletedTask;

        var onGround = span[off] != 0;
        if (session.Player != null)
            session.Player.OnGround = onGround;

        return ValueTask.CompletedTask;
    }

    private static ValueTask HandlePlayerPosition(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 25 > span.Length) // f64*3 + bool
            return ValueTask.CompletedTask;

        var x = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var y = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var z = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var onGround = span[off] != 0;

        if (session.Player != null)
        {
            session.Player.X = x;
            session.Player.Y = y;
            session.Player.Z = z;
            session.Player.OnGround = onGround;
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask HandlePlayerPositionAndLook(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 33 > span.Length) // f64*3 + f32*2 + bool
            return ValueTask.CompletedTask;

        var x = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var y = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var z = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var yaw = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var pitch = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var onGround = span[off] != 0;

        if (session.Player != null)
        {
            session.Player.X = x;
            session.Player.Y = y;
            session.Player.Z = z;
            session.Player.Yaw = yaw;
            session.Player.Pitch = pitch;
            session.Player.OnGround = onGround;
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask HandlePlayerLook(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 9 > span.Length) // f32*2 + bool
            return ValueTask.CompletedTask;

        var yaw = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var pitch = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var onGround = span[off] != 0;

        if (session.Player != null)
        {
            session.Player.Yaw = yaw;
            session.Player.Pitch = pitch;
            session.Player.OnGround = onGround;
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask HandleHeldItemChange(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 2 > span.Length)
            return ValueTask.CompletedTask;

        var slotId = BinaryPrimitives.ReadInt16BigEndian(span[off..]);
        Console.WriteLine($"[M1] HeldItemChange: sessionId={session.SessionId}, slotId={slotId}");

        return ValueTask.CompletedTask;
    }
}
