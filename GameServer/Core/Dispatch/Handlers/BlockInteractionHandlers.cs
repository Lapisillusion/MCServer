using System.Buffers.Binary;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Interactions;
using GameServer.Network;
using GameServer.Network.Backend;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Core.Dispatch.Handlers;

internal static class BlockInteractionHandlers
{
    public static ValueTask HandlePlayerDigging(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var status))
            return ValueTask.CompletedTask;
        if (off + 9 > span.Length)
            return ValueTask.CompletedTask;
        var position = BinaryPrimitives.ReadInt64BigEndian(span[off..]);
        off += 8;
        var face = span[off];

        if (session.Player == null || !BlockInteractionService.IsWithinReach(
                session.Player.X, session.Player.Y, session.Player.Z, position))
        {
            Warn("BlockInteraction", context, "Rejected out-of-reach dig");
            return ValueTask.CompletedTask;
        }

        var result = BlockInteractionService.ProcessDig(
            HandlerContext.ChunkProvider, position, (byte)status, out var error);

        if (error != null)
        {
            var bx = (int)(position >> 38);
            var by = (int)(position << 52 >> 52);
            var bz = (int)(position << 38 >> 38);
            Warn("BlockInteraction", context,
                "PlayerDigging failed {BlockX} {BlockY} {BlockZ} {Face} {Error}", bx, by, bz, face, error);
            return ValueTask.CompletedTask;
        }

        if (result != null)
        {
            session.EnqueueOutput(result);
            BroadcastToOthers(session, result);
            var bx = (int)(position >> 38);
            var by = (int)(position << 52 >> 52);
            var bz = (int)(position << 38 >> 38);
            Info("BlockInteraction", context,
                "Block broken {BlockX} {BlockY} {BlockZ} {Face}", bx, by, bz, face);
        }

        return ValueTask.CompletedTask;
    }

    public static ValueTask HandlePlayerBlockPlacement(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 8 > span.Length)
            return ValueTask.CompletedTask;
        var position = BinaryPrimitives.ReadInt64BigEndian(span[off..]);
        off += 8;

        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var face))
            return ValueTask.CompletedTask;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var hand))
            return ValueTask.CompletedTask;
        if (off + 12 > span.Length)
            return ValueTask.CompletedTask;
        off += 12;

        if (session.Player == null || hand != 0 || !BlockInteractionService.IsWithinReach(
                session.Player.X, session.Player.Y, session.Player.Z, position, face))
        {
            Warn("BlockInteraction", context, "Rejected invalid or out-of-reach placement");
            return ValueTask.CompletedTask;
        }

        var heldItem = session.Player.Hotbar.GetSlot(session.Player.Hotbar.SelectedSlot);
        if (heldItem == null)
        {
            Warn("BlockInteraction", context, "Rejected placement with empty selected hotbar slot");
            return ValueTask.CompletedTask;
        }

        var result = BlockInteractionService.ProcessPlace(
            HandlerContext.ChunkProvider, position, face, heldItem.BlockState, out var error);

        if (error != null)
        {
            var bx = (int)(position >> 38);
            var by = (int)(position << 52 >> 52);
            var bz = (int)(position << 38 >> 38);
            Warn("BlockInteraction", context,
                "PlayerBlockPlacement failed {BlockX} {BlockY} {BlockZ} {Face} {Error}", bx, by, bz, face, error);
            return ValueTask.CompletedTask;
        }

        if (result != null)
        {
            // Only consume after the world mutation has succeeded.
            if (!session.Player.Hotbar.TryConsumeSelectedBlock(out _))
                throw new InvalidOperationException("Selected item disappeared during placement.");

            session.EnqueueOutput(result);
            var hotbarSlot = session.Player.Hotbar.SelectedSlot;
            session.EnqueueOutput(S2CPacketBuilders.BuildSetSlot(0, (short)(36 + hotbarSlot),
                session.Player.Hotbar.GetSlot(hotbarSlot)));
            BroadcastToOthers(session, result);
            var bx = (int)(position >> 38);
            var by = (int)(position << 52 >> 52);
            var bz = (int)(position << 38 >> 38);
            Info("BlockInteraction", context,
                "Block placed {BlockX} {BlockY} {BlockZ} {Face}", bx, by, bz, face);
        }

        return ValueTask.CompletedTask;
    }

    public static ValueTask HandleAnimation(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var hand))
            return ValueTask.CompletedTask;

        if (session.Player == null)
            return ValueTask.CompletedTask;

        var animPacket = S2CPacketBuilders.BuildAnimation(session.Player.EntityId, animationType: 0);
        BroadcastToOthers(session, animPacket);

        return ValueTask.CompletedTask;
    }

    private static void BroadcastToOthers(SessionContext source, byte[] packet)
    {
        foreach (var (sid, other) in HandlerContext.Sessions.All)
        {
            if (sid != source.SessionId && other.State == GameSessionState.Play && !other.Closed)
                other.EnqueueOutput(packet);
        }
    }
}
