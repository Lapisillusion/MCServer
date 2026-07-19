using Common.MC;
using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick.Stages;

/// <summary>
/// Drains each session's input queue and dispatches frames to PlayPacket handlers.
/// Handlers run on the tick thread and enqueue output via session.EnqueueOutput().
/// </summary>
public sealed class InputCollectStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly PlayPacketDispatcher _dispatcher;

    public TickPipelineStage Stage => TickPipelineStage.InputCollect;

    public InputCollectStage(SessionRegistry sessions, PlayPacketDispatcher dispatcher)
    {
        _sessions = sessions;
        _dispatcher = dispatcher;
    }

    public async ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        foreach (var (sid, session) in _sessions.All)
        {
            if (session.Closed || session.CloseRequested) continue;

            var baseContext = RuntimeLogContext.Empty
                .WithSessionId(sid.ToString())
                .WithPlayerName(session.PlayerName)
                .WithTickId(tickNumber)
                .WithStage(nameof(TickPipelineStage.InputCollect));

            // Propagate Layer 3 entity fields from PlayerContext
            if (session.Player != null)
            {
                baseContext = baseContext
                    .WithEntityId(session.Player.EntityId)
                    .WithDimension(session.Player.Dimension)
                    .WithGamemode(session.Player.Gamemode);
            }

            var processed = 0;
            while (session.TryDequeueInput(out var frame))
            {
                var packetName = PacketNameResolver.GetPlayPacketName(frame.PacketId, isC2S: true);
                var context = baseContext
                    .WithPacketId($"0x{frame.PacketId:X2}")
                    .WithPacketName(packetName)
                    .WithDirection("C2S");

                await _dispatcher.DispatchOrIgnoreAsync(
                    session, frame.PacketId, context, frame.Frame, ct);
                processed++;
            }

            if (processed > 32)
                Warn("InputCollect", sid, session.PlayerName,
                    $"Tick #{tickNumber}: processed {processed} frames — input queue backlog");
        }
    }

    // ── Helpers ────────────────────────────────────────────
}
