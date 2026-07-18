using System.Buffers.Binary;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Network.Backend;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Core.Dispatch.Handlers;

internal static class HeldItemChangeHandler
{
    public static ValueTask Handle(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;
        if (off + 2 > span.Length)
            return ValueTask.CompletedTask;

        var slotId = BinaryPrimitives.ReadInt16BigEndian(span[off..]);
        Info("PlayPacket", context,
            "HeldItemChange {SlotId}", slotId);

        return ValueTask.CompletedTask;
    }
}
