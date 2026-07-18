using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Players;

namespace GameServer.Replication;

/// <summary>
/// Tracks entity visibility per observer. For each observer session,
/// maintains the set of visible entity IDs and their last-known positions.
/// Each tick, the ReplicationStage calls UpdateVisibility() to compute
/// which entities to spawn, despawn, or update for each observer.
///
/// Visibility rules:
/// - Same dimension only
/// - Within ViewDistance blocks (Euclidean, horizontal+vertical)
/// - Observer session must be in Play state
/// </summary>
public sealed class EntityTracker
{
    /// <summary>Horizontal+vertical view distance in blocks.</summary>
    private const double ViewDistance = 64.0;
    private const double ViewDistanceSq = ViewDistance * ViewDistance;

    /// <summary>Minimum position delta (blocks) to trigger a move update.</summary>
    private const double MinMoveDelta = 0.01;

    /// <summary>Minimum yaw/pitch delta (degrees) to trigger a look update.</summary>
    private const float MinLookDelta = 1.0f;

    /// <summary>If the delta exceeds this many blocks, use EntityTeleport instead of EntityRelativeMove.</summary>
    private const double TeleportThreshold = 8.0;

    private readonly Dictionary<long, HashSet<int>> _visible = new();
    private readonly Dictionary<(long ObserverId, int EntityId), LastKnownState> _lastKnown = new();

    // ── Public API ─────────────────────────────────────────

    /// <summary>
    /// Compute visibility changes for a single observer against all active players.
    /// Returns lists of entity IDs to spawn, despawn, and entities with position deltas
    /// (including newly-spawned entities as large deltas so the first packet is a teleport).
    /// </summary>
    public VisibilityResult UpdateVisibility(
        SessionContext observer,
        IReadOnlyList<SessionContext> allSessions)
    {
        if (observer.Player == null)
            return VisibilityResult.Empty;

        var spawned = new List<PlayerContext>();
        var despawned = new List<int>();
        var moved = new List<MoveEntry>();

        // Ensure the observer has a visibility set
        if (!_visible.TryGetValue(observer.SessionId, out var visibleSet))
        {
            visibleSet = new HashSet<int>();
            _visible[observer.SessionId] = visibleSet;
        }

        // Build the set of currently visible entities
        var currentVisible = new HashSet<int>();

        foreach (var other in allSessions)
        {
            if (other.SessionId == observer.SessionId) continue;
            if (other.State != GameSessionState.Play) continue;
            if (other.Closed) continue;
            if (other.Player == null) continue;

            // Dimension check
            if (observer.Player.Dimension != other.Player.Dimension) continue;

            // Distance check
            var dx = observer.Player.X - other.Player.X;
            var dy = observer.Player.Y - other.Player.Y;
            var dz = observer.Player.Z - other.Player.Z;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > ViewDistanceSq) continue;

            currentVisible.Add(other.Player.EntityId);

            // Check if newly visible
            if (!visibleSet.Contains(other.Player.EntityId))
            {
                spawned.Add(other.Player);
                visibleSet.Add(other.Player.EntityId);
            }
            else
            {
                // Compute delta from last-known position
                var key = (observer.SessionId, other.Player.EntityId);
                if (_lastKnown.TryGetValue(key, out var last))
                {
                    var d = Math.Abs(other.Player.X - last.X) +
                            Math.Abs(other.Player.Y - last.Y) +
                            Math.Abs(other.Player.Z - last.Z);
                    var dr = Math.Abs(other.Player.Yaw - last.Yaw) +
                             Math.Abs(other.Player.Pitch - last.Pitch);

                    if (d >= MinMoveDelta || dr >= MinLookDelta)
                    {
                        moved.Add(new MoveEntry(other.Player, last,
                            Math.Sqrt(dx * dx + dy * dy + dz * dz) > TeleportThreshold));
                    }
                }
                else
                {
                    // First time tracking — send full teleport
                    moved.Add(new MoveEntry(other.Player, null, IsTeleport: true));
                }
            }
        }

        // Find despawned (were visible, no longer are)
        foreach (var entityId in visibleSet.ToArray())
        {
            if (!currentVisible.Contains(entityId))
            {
                despawned.Add(entityId);
                visibleSet.Remove(entityId);
                _lastKnown.Remove((observer.SessionId, entityId));
            }
        }

        // Record positions for all currently visible
        foreach (var entityId in currentVisible)
        {
            // Find the player context for this entity
            PlayerContext? playerCtx = null;
            foreach (var s in allSessions)
            {
                if (s.Player?.EntityId == entityId)
                {
                    playerCtx = s.Player;
                    break;
                }
            }
            if (playerCtx != null)
            {
                _lastKnown[(observer.SessionId, entityId)] = new LastKnownState(
                    playerCtx.X, playerCtx.Y, playerCtx.Z,
                    playerCtx.Yaw, playerCtx.Pitch, playerCtx.OnGround);
            }
        }

        return new VisibilityResult(spawned, despawned, moved);
    }

    /// <summary>Clean up all tracking data for a disconnected observer.</summary>
    public void RemoveObserver(long sessionId)
    {
        _visible.Remove(sessionId);
        var keys = _lastKnown.Keys.Where(k => k.ObserverId == sessionId).ToArray();
        foreach (var key in keys)
            _lastKnown.Remove(key);
    }

    /// <summary>Clean up tracking data for a disconnected player entity.</summary>
    public void RemoveEntity(int entityId)
    {
        foreach (var (observerId, visibleSet) in _visible)
        {
            visibleSet.Remove(entityId);
        }
        var keys = _lastKnown.Keys.Where(k => k.EntityId == entityId).ToArray();
        foreach (var key in keys)
            _lastKnown.Remove(key);
    }

    // ── Types ──────────────────────────────────────────────

    public readonly record struct LastKnownState(
        double X, double Y, double Z, float Yaw, float Pitch, bool OnGround);

    public readonly record struct MoveEntry(
        PlayerContext Player,
        LastKnownState? Last,
        bool IsTeleport);

    public readonly record struct VisibilityResult(
        List<PlayerContext> Spawned,
        List<int> Despawned,
        List<MoveEntry> Moved)
    {
        public bool IsEmpty => Spawned.Count == 0 && Despawned.Count == 0 && Moved.Count == 0;
        public static VisibilityResult Empty => new(new List<PlayerContext>(), new List<int>(), new List<MoveEntry>());
    }
}
