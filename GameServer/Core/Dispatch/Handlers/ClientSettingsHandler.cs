using System.Text;
using GameServer.Core.Diagnostics;
using GameServer.Core.Session;
using GameServer.Network.Backend;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Core.Dispatch.Handlers;

internal static class ClientSettingsHandler
{
    public static ValueTask Handle(
        SessionContext session, in RuntimeLogContext context, in PlayPacketRouteKey route,
        ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (!TeleportConfirmHandler.SkipFrameHeader(frame, out var off))
            return ValueTask.CompletedTask;

        var span = frame.Span;

        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var localeLen)
            || localeLen < 0 || localeLen > 64 || off + localeLen > span.Length)
            return ValueTask.CompletedTask;

        var locale = Encoding.UTF8.GetString(span.Slice(off, localeLen));
        off += localeLen;

        if (off >= span.Length) return ValueTask.CompletedTask;
        var viewDistance = (sbyte)span[off++];

        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var chatFlags))
            return ValueTask.CompletedTask;

        if (off >= span.Length) return ValueTask.CompletedTask;
        var chatColors = span[off++] != 0;

        if (off >= span.Length) return ValueTask.CompletedTask;
        var skinParts = span[off++];

        if (!McPlayFrameCodec.TryReadVarInt(span, ref off, out var mainHand))
            return ValueTask.CompletedTask;

        var appliedViewDistance = session.Player?.ChunkView.SetRequestedRadius(
            viewDistance, HandlerContext.Options.MaxChunkViewRadius);

        Info("PlayPacket", context,
            "ClientSettings received {Locale} requestedView={ViewDistance} appliedView={AppliedViewDistance} {ChatFlags} {ChatColors} {SkinParts} {MainHand}",
            locale, viewDistance, appliedViewDistance ?? 0, chatFlags, chatColors, skinParts, mainHand);
        return ValueTask.CompletedTask;
    }
}
