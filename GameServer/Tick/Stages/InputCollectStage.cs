using Common.MC;
using GameServer.Application;
using GameServer.Core.Diagnostics;
using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Network;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick.Stages;

/// <summary>Applies a fair per-session packet budget before dispatching input on the tick thread.</summary>
public sealed class InputCollectStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly PlayPacketDispatcher _dispatcher;
    private readonly int _maxFramesPerSession;
    private readonly TickMetrics? _metrics;

    public TickPipelineStage Stage => TickPipelineStage.InputCollect;

    public InputCollectStage(
        SessionRegistry sessions,
        PlayPacketDispatcher dispatcher,
        GameServerOptions? options = null,
        TickMetrics? metrics = null)
    {
        _sessions = sessions;
        _dispatcher = dispatcher;
        _maxFramesPerSession = Math.Max(1, (options ?? GameServerOptions.CreateDefault()).MaxInputFramesPerTick);
        _metrics = metrics;
    }

    public async ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        foreach (var (sid, session) in _sessions.All)
        {
            if (session.Closed || session.CloseRequested || session.IncomingCount == 0)
                continue;

            var baseContext = RuntimeLogContext.Empty
                .WithSessionId(sid.ToString())
                .WithPlayerName(session.PlayerName)
                .WithTickId(tickNumber)
                .WithStage(nameof(TickPipelineStage.InputCollect));

            if (session.Player != null)
            {
                baseContext = baseContext
                    .WithEntityId(session.Player.EntityId)
                    .WithDimension(session.Player.Dimension)
                    .WithGamemode(session.Player.Gamemode);
            }

            var processed = 0;
            while (processed < _maxFramesPerSession && session.TryDequeueInput(out var frame))
            {
                var context = baseContext
                    .WithPacketId($"0x{frame.PacketId:X2}")
                    .WithPacketName(PacketNameResolver.GetPlayPacketName(frame.PacketId, isC2S: true))
                    .WithDirection("C2S");

                await _dispatcher.DispatchOrIgnoreAsync(
                    session, frame.PacketId, context, frame.Frame, ct);
                processed++;
            }

            _metrics?.AddInputFrames(processed);
            if (session.IncomingCount > 0 && tickNumber % 20 == 0)
            {
                Warn("InputCollect", sid, session.PlayerName,
                    $"Tick #{tickNumber}: per-player input cap={_maxFramesPerSession}, remaining={session.IncomingCount}");
            }
        }
    }
}
