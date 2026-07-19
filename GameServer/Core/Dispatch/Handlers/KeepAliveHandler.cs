using System.Buffers.Binary;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Core.Dispatch.Handlers;

internal static class KeepAliveHandler
{
    public static ValueTask Handle(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 8 > span.Length)
            return ValueTask.CompletedTask;

        var keepAliveId = BinaryPrimitives.ReadInt64BigEndian(span[off..]);
        if (session.Liveness.TryAcknowledge(
                keepAliveId, session.TimeProvider.GetUtcNow(), out var rtt))
        {
            Info("KeepAlive", context,
                "KeepAlive acknowledged {KeepAliveId}, RTT={RttMs:F1}ms",
                keepAliveId, rtt.TotalMilliseconds);
        }
        else
        {
            Warn("KeepAlive", context,
                "Unexpected or stale KeepAlive response {KeepAliveId}, pending={PendingKeepAliveId}",
                keepAliveId, session.Liveness.PendingKeepAliveId ?? -1L);
        }

        return ValueTask.CompletedTask;
    }
}
