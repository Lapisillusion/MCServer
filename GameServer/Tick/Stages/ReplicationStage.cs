using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Network;
using GameServer.Players;
using GameServer.Replication;

namespace GameServer.Tick.Stages;

/// <summary>Cross-player visibility and movement replication with tick-owned reusable snapshots.</summary>
public sealed class ReplicationStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly EntityTracker _tracker;
    private readonly List<SessionContext> _active = new();
    private readonly Dictionary<int, PlayerContext> _playersByEntityId = new();

    public TickPipelineStage Stage => TickPipelineStage.Replication;

    public ReplicationStage(SessionRegistry sessions, EntityTracker tracker)
    {
        _sessions = sessions;
        _tracker = tracker;
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        _active.Clear();
        _playersByEntityId.Clear();
        foreach (var (_, session) in _sessions.All)
        {
            if (session.Closed || session.CloseRequested ||
                session.State != GameSessionState.Play || session.Player == null)
                continue;

            _active.Add(session);
            _playersByEntityId[session.Player.EntityId] = session.Player;
        }

        if (_active.Count <= 1)
            return ValueTask.CompletedTask;

        foreach (var observer in _active)
        {
            var result = _tracker.UpdateVisibility(observer, _active, _playersByEntityId);
            if (result.IsEmpty)
                continue;

            if (result.Despawned.Count > 0)
                observer.EnqueueOutput(S2CPacketBuilders.BuildDestroyEntities(result.Despawned));

            foreach (var player in result.Spawned)
            {
                observer.EnqueueOutput(S2CPacketBuilders.BuildSpawnPlayer(
                    player.EntityId, player.PlayerId, player.X, player.Y, player.Z,
                    AngleToByte(player.Yaw), AngleToByte(player.Pitch)));
            }

            foreach (var move in result.Moved)
            {
                var player = move.Player;
                if (move.IsTeleport)
                {
                    observer.EnqueueOutput(S2CPacketBuilders.BuildEntityTeleport(
                        player.EntityId, player.X, player.Y, player.Z,
                        player.Yaw, player.Pitch, player.OnGround));
                    continue;
                }

                if (move.Last == null)
                    continue;

                var last = move.Last.Value;
                var dx = S2CPacketBuilders.EncodeDelta(player.X, last.X);
                var dy = S2CPacketBuilders.EncodeDelta(player.Y, last.Y);
                var dz = S2CPacketBuilders.EncodeDelta(player.Z, last.Z);
                var lookChanged = Math.Abs(player.Yaw - last.Yaw) >= 1f ||
                                  Math.Abs(player.Pitch - last.Pitch) >= 1f;

                if (lookChanged && (dx != 0 || dy != 0 || dz != 0))
                {
                    observer.EnqueueOutput(S2CPacketBuilders.BuildEntityLookAndRelativeMove(
                        player.EntityId, dx, dy, dz,
                        AngleToByte(player.Yaw), AngleToByte(player.Pitch), player.OnGround));
                }
                else if (dx != 0 || dy != 0 || dz != 0)
                {
                    observer.EnqueueOutput(S2CPacketBuilders.BuildEntityRelativeMove(
                        player.EntityId, dx, dy, dz, player.OnGround));
                }
                else if (lookChanged)
                {
                    observer.EnqueueOutput(S2CPacketBuilders.BuildEntityLook(
                        player.EntityId, AngleToByte(player.Yaw), AngleToByte(player.Pitch), player.OnGround));
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private static byte AngleToByte(float angle)
    {
        var wrapped = angle % 360f;
        if (wrapped < 0)
            wrapped += 360f;
        return (byte)(wrapped * 256f / 360f);
    }
}
