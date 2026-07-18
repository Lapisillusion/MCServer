using System.Buffers.Binary;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Movement;
using GameServer.Network;
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
        if (off >= span.Length)
            return ValueTask.CompletedTask;

        var onGround = span[off] != 0;
        if (session.Player != null)
        {
            session.Player.OnGround = onGround;
            Info("Movement", context,
                "Player position update {OnGround}", onGround);
        }

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
        var onGround = span[off] != 0;

        if (session.Player != null)
        {
            var config = MovementConfig.Default;
            var reason = MovementValidator.ValidatePosition(x, y, z,
                session.Player.X, session.Player.Y, session.Player.Z, config);

            if (reason != null)
            {
                Warn("Movement", context,
                    "PlayerPosition rejected {Reason}, force-syncing", reason);
                var tp = S2CPacketBuilders.BuildEntityTeleport(
                    session.Player.EntityId,
                    session.Player.X, session.Player.Y, session.Player.Z,
                    session.Player.Yaw, session.Player.Pitch, session.Player.OnGround);
                session.EnqueueOutput(tp);
                return ValueTask.CompletedTask;
            }

            session.Player.X = x;
            session.Player.Y = y;
            session.Player.Z = z;
            session.Player.OnGround = onGround;
            Info("Movement", context,
                "PlayerPosition {X:F2} {Y:F2} {Z:F2} {OnGround}", x, y, z, onGround);
        }

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
        var onGround = span[off] != 0;

        if (session.Player != null)
        {
            var config = MovementConfig.Default;
            var reason = MovementValidator.ValidatePositionAndLook(
                x, y, z, yaw, pitch,
                session.Player.X, session.Player.Y, session.Player.Z,
                session.Player.Yaw, session.Player.Pitch, config);

            if (reason != null)
            {
                Warn("Movement", context,
                    "PlayerPositionAndLook rejected {Reason}, force-syncing", reason);
                var tp = S2CPacketBuilders.BuildEntityTeleport(
                    session.Player.EntityId,
                    session.Player.X, session.Player.Y, session.Player.Z,
                    session.Player.Yaw, session.Player.Pitch, session.Player.OnGround);
                session.EnqueueOutput(tp);
                return ValueTask.CompletedTask;
            }

            session.Player.X = x;
            session.Player.Y = y;
            session.Player.Z = z;
            session.Player.Yaw = yaw;
            session.Player.Pitch = pitch;
            session.Player.OnGround = onGround;
            Info("Movement", context,
                "PlayerPositionAndLook {X:F2} {Y:F2} {Z:F2} {Yaw:F2} {Pitch:F2} {OnGround}", x, y, z, yaw, pitch, onGround);
        }

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
        var onGround = span[off] != 0;

        if (session.Player != null)
        {
            var reason = MovementValidator.ValidateLook(yaw, pitch);
            if (reason != null)
            {
                Warn("Movement", context,
                    "PlayerLook rejected {Reason}, force-syncing", reason);
                var tp = S2CPacketBuilders.BuildEntityTeleport(
                    session.Player.EntityId,
                    session.Player.X, session.Player.Y, session.Player.Z,
                    session.Player.Yaw, session.Player.Pitch, session.Player.OnGround);
                session.EnqueueOutput(tp);
                return ValueTask.CompletedTask;
            }

            session.Player.Yaw = yaw;
            session.Player.Pitch = pitch;
            session.Player.OnGround = onGround;
            Info("Movement", context,
                "PlayerLook {Yaw:F2} {Pitch:F2} {OnGround}", yaw, pitch, onGround);
        }

        return ValueTask.CompletedTask;
    }
}
