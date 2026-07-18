using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Network;
using GameServer.Replication;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Tick.Stages;

/// <summary>
/// Cross-player entity synchronization. Runs after Simulate and before NetworkFlush.
/// For each observer, computes visibility changes via EntityTracker and enqueues
/// SpawnPlayer / DestroyEntities / movement packets via session.EnqueueOutput().
///
/// Packet selection for movement:
///   - First sighting or large delta (>8 blocks): EntityTeleport (0x4C)
///   - Small delta + look changed: EntityLookAndRelativeMove (0x27)
///   - Small delta only: EntityRelativeMove (0x26)
///   - Look changed only: EntityLook (0x28)
/// </summary>
public sealed class ReplicationStage : ITickStage
{
    private readonly SessionRegistry _sessions;
    private readonly EntityTracker _tracker;

    public TickPipelineStage Stage => TickPipelineStage.Replication;

    public ReplicationStage(SessionRegistry sessions, EntityTracker tracker)
    {
        _sessions = sessions;
        _tracker = tracker;
    }

    public ValueTask ExecuteAsync(long tickNumber, CancellationToken ct)
    {
        // Build a snapshot of active play sessions
        var active = new List<SessionContext>();
        foreach (var (_, session) in _sessions.All)
        {
            if (!session.Closed && session.State == GameSessionState.Play && session.Player != null)
                active.Add(session);
        }

        if (active.Count <= 1)
            return ValueTask.CompletedTask; // nothing to replicate with only 1 player

        var activeReadOnly = (IReadOnlyList<SessionContext>)active;

        foreach (var observer in active)
        {
            var result = _tracker.UpdateVisibility(observer, activeReadOnly);
            if (result.IsEmpty) continue;

            // ── Despawn entities no longer visible ──────────
            if (result.Despawned.Count > 0)
            {
                var destroyPacket = S2CPacketBuilders.BuildDestroyEntities(result.Despawned.ToArray());
                observer.EnqueueOutput(destroyPacket);
            }

            // ── Spawn newly visible player entities ─────────
            foreach (var player in result.Spawned)
            {
                var yawByte = AngleToByte(player.Yaw);
                var pitchByte = AngleToByte(player.Pitch);
                var spawnPacket = S2CPacketBuilders.BuildSpawnPlayer(
                    player.EntityId, player.PlayerId, player.X, player.Y, player.Z,
                    yawByte, pitchByte);
                observer.EnqueueOutput(spawnPacket);
            }

            // ── Movement updates for tracked entities ───────
            foreach (var move in result.Moved)
            {
                var p = move.Player;
                if (move.IsTeleport)
                {
                    var tp = S2CPacketBuilders.BuildEntityTeleport(
                        p.EntityId, p.X, p.Y, p.Z, p.Yaw, p.Pitch, p.OnGround);
                    observer.EnqueueOutput(tp);
                }
                else if (move.Last != null)
                {
                    var last = move.Last.Value;
                    var dx = S2CPacketBuilders.EncodeDelta(p.X, last.X);
                    var dy = S2CPacketBuilders.EncodeDelta(p.Y, last.Y);
                    var dz = S2CPacketBuilders.EncodeDelta(p.Z, last.Z);
                    var lookChanged = Math.Abs(p.Yaw - last.Yaw) >= 1f || Math.Abs(p.Pitch - last.Pitch) >= 1f;

                    if (lookChanged && (dx != 0 || dy != 0 || dz != 0))
                    {
                        var pkt = S2CPacketBuilders.BuildEntityLookAndRelativeMove(
                            p.EntityId, dx, dy, dz,
                            AngleToByte(p.Yaw), AngleToByte(p.Pitch), p.OnGround);
                        observer.EnqueueOutput(pkt);
                    }
                    else if (dx != 0 || dy != 0 || dz != 0)
                    {
                        var pkt = S2CPacketBuilders.BuildEntityRelativeMove(
                            p.EntityId, dx, dy, dz, p.OnGround);
                        observer.EnqueueOutput(pkt);
                    }
                    else if (lookChanged)
                    {
                        var pkt = S2CPacketBuilders.BuildEntityLook(
                            p.EntityId, AngleToByte(p.Yaw), AngleToByte(p.Pitch), p.OnGround);
                        observer.EnqueueOutput(pkt);
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Convert float angle (degrees) to protocol byte (0-255).</summary>
    private static byte AngleToByte(float angle)
    {
        var wrapped = angle % 360f;
        if (wrapped < 0) wrapped += 360f;
        return (byte)(wrapped * 256f / 360f);
    }
}
