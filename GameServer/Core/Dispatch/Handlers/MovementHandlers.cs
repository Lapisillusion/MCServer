using System.Buffers.Binary;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Movement;
using GameServer.Network.Backend;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Core.Dispatch.Handlers;

internal static class MovementHandlers
{
    public static ValueTask HandlePlayer(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off >= span.Length || session.Player == null)
            return ValueTask.CompletedTask;

        var clientOnGround = span[off] != 0;
        var serverOnGround = HandlerContext.Movement.RefreshGroundState(session.Player);
        Info("Movement", context,
            "Ground state client={ClientOnGround} server={ServerOnGround}",
            clientOnGround, serverOnGround);
        return ValueTask.CompletedTask;
    }

    public static ValueTask HandlePlayerPosition(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 25 > span.Length)
            return ValueTask.CompletedTask;

        var x = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var y = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var z = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var clientOnGround = span[off] != 0;

        ApplyPosition(session, context, x, y, z, clientOnGround);
        return ValueTask.CompletedTask;
    }

    public static ValueTask HandlePlayerPositionAndLook(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 33 > span.Length)
            return ValueTask.CompletedTask;

        var x = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var y = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var z = BinaryPrimitives.ReadDoubleBigEndian(span[off..]); off += 8;
        var yaw = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var pitch = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var clientOnGround = span[off] != 0;

        var player = session.Player;
        if (player == null)
            return ValueTask.CompletedTask;

        var reason = MovementValidator.ValidatePositionAndLook(
            x, y, z, yaw, pitch,
            player.X, player.Y, player.Z,
            player.Yaw, player.Pitch,
            MovementConfig.Default);
        if (reason != null)
        {
            RejectAndCorrect(session, context, "PlayerPositionAndLook", reason);
            return ValueTask.CompletedTask;
        }

        player.Yaw = yaw;
        player.Pitch = pitch;
        ApplyValidatedPosition(session, context, x, y, z, clientOnGround, "PlayerPositionAndLook");
        return ValueTask.CompletedTask;
    }

    public static ValueTask HandlePlayerLook(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 9 > span.Length)
            return ValueTask.CompletedTask;

        var yaw = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var pitch = BinaryPrimitives.ReadSingleBigEndian(span[off..]); off += 4;
        var clientOnGround = span[off] != 0;

        var player = session.Player;
        if (player == null)
            return ValueTask.CompletedTask;

        var reason = MovementValidator.ValidateLook(yaw, pitch);
        if (reason != null)
        {
            RejectAndCorrect(session, context, "PlayerLook", reason);
            return ValueTask.CompletedTask;
        }

        player.Yaw = yaw;
        player.Pitch = pitch;
        var serverOnGround = HandlerContext.Movement.RefreshGroundState(player);
        Info("Movement", context,
            "PlayerLook yaw={Yaw:F2} pitch={Pitch:F2} clientGround={ClientOnGround} serverGround={ServerOnGround}",
            yaw, pitch, clientOnGround, serverOnGround);
        return ValueTask.CompletedTask;
    }

    private static void ApplyPosition(
        SessionContext session,
        in RuntimeLogContext context,
        double x,
        double y,
        double z,
        bool clientOnGround)
    {
        var player = session.Player;
        if (player == null)
            return;

        var reason = MovementValidator.ValidatePosition(
            x, y, z,
            player.X, player.Y, player.Z,
            MovementConfig.Default);
        if (reason != null)
        {
            RejectAndCorrect(session, context, "PlayerPosition", reason);
            return;
        }

        ApplyValidatedPosition(session, context, x, y, z, clientOnGround, "PlayerPosition");
    }

    private static void ApplyValidatedPosition(
        SessionContext session,
        in RuntimeLogContext context,
        double x,
        double y,
        double z,
        bool clientOnGround,
        string packetName)
    {
        var player = session.Player!;
        if (player.AwaitingTeleportConfirm)
        {
            Warn("Movement", context,
                "Ignored {PacketName} while awaiting TeleportConfirm {TeleportId}",
                packetName, player.TeleportId);
            return;
        }

        var result = HandlerContext.Movement.MoveTo(player, x, y, z, context.TickId);
        if (result.WasClipped)
        {
            Warn("Movement", context,
                "{PacketName} clipped requested=({RequestedX:F3},{RequestedY:F3},{RequestedZ:F3}) resolved=({ResolvedX:F3},{ResolvedY:F3},{ResolvedZ:F3})",
                packetName,
                x, y, z,
                result.X, result.Y, result.Z);
            PlayerMovementService.EnqueuePositionCorrection(session);
            return;
        }

        Info("Movement", context,
            "{PacketName} accepted pos=({X:F3},{Y:F3},{Z:F3}) clientGround={ClientOnGround} serverGround={ServerOnGround}",
            packetName,
            result.X, result.Y, result.Z,
            clientOnGround, player.OnGround);
    }

    private static void RejectAndCorrect(
        SessionContext session,
        in RuntimeLogContext context,
        string packetName,
        string reason)
    {
        Warn("Movement", context,
            "{PacketName} rejected {Reason}; correcting client", packetName, reason);
        PlayerMovementService.EnqueuePositionCorrection(session);
    }
}
