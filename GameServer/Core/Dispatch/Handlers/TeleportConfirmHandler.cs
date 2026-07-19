using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Network.Backend;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Core.Dispatch.Handlers;

internal static class TeleportConfirmHandler
{
    public static ValueTask Handle(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var teleportId))
            return ValueTask.CompletedTask;

        Info("PlayPacket", context,
            "TeleportConfirm received {TeleportId}", teleportId);

        if (session.Player == null || session.Player.ChunksSent)
            return ValueTask.CompletedTask;

        if (teleportId != session.Player.TeleportId)
        {
            Warn("PlayPacket", context,
                "Ignored unexpected TeleportConfirm {TeleportId}; expected {ExpectedTeleportId}",
                teleportId, session.Player.TeleportId);
            return ValueTask.CompletedTask;
        }

        HandlerContext.ChunkStream.InitializeView(session);
        session.Player.ChunksSent = true;
        return ValueTask.CompletedTask;
    }

    internal static bool SkipFrameHeader(ReadOnlyMemory<byte> frame, out int payloadOffset)
    {
        var span = frame.Span;
        var off = 0;
        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var payloadLen)
            || payloadLen < 0 || payloadLen > McPlayFrameCodec.MaxFramePayload)
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
}
