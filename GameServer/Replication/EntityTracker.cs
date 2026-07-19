using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Players;

namespace GameServer.Replication;

/// <summary>
/// Tick-thread-owned player visibility tracker. Per-observer scratch collections are retained and
/// cleared between ticks, avoiding the previous per-observer List/HashSet/ToArray allocation chain.
/// </summary>
public sealed class EntityTracker
{
    private const double ViewDistanceSq = 64.0 * 64.0;
    private const double MinMoveDelta = 0.01;
    private const float MinLookDelta = 1.0f;
    private const double TeleportThresholdSq = 8.0 * 8.0;

    private readonly Dictionary<long, ObserverState> _observers = new();
    private readonly Dictionary<(long ObserverId, int EntityId), LastKnownState> _lastKnown = new();
    private readonly Dictionary<int, PlayerContext> _lookupScratch = new();
    private readonly List<(long ObserverId, int EntityId)> _keyScratch = new();

    public VisibilityResult UpdateVisibility(
        SessionContext observer,
        IReadOnlyList<SessionContext> allSessions)
    {
        _lookupScratch.Clear();
        foreach (var session in allSessions)
        {
            if (session.Player != null)
                _lookupScratch[session.Player.EntityId] = session.Player;
        }
        return UpdateVisibility(observer, allSessions, _lookupScratch);
    }

    public VisibilityResult UpdateVisibility(
        SessionContext observer,
        IReadOnlyList<SessionContext> allSessions,
        IReadOnlyDictionary<int, PlayerContext> playersByEntityId)
    {
        var observerPlayer = observer.Player;
        if (observerPlayer == null)
            return VisibilityResult.Empty;

        if (!_observers.TryGetValue(observer.SessionId, out var state))
        {
            state = new ObserverState();
            _observers.Add(observer.SessionId, state);
        }

        state.ClearScratch();
        var visibleSet = state.Visible;
        var currentVisible = state.CurrentVisible;

        foreach (var other in allSessions)
        {
            var otherPlayer = other.Player;
            if (other.SessionId == observer.SessionId ||
                other.State != GameSessionState.Play || other.Closed || other.CloseRequested ||
                otherPlayer == null || observerPlayer.Dimension != otherPlayer.Dimension)
                continue;

            var dx = observerPlayer.X - otherPlayer.X;
            var dy = observerPlayer.Y - otherPlayer.Y;
            var dz = observerPlayer.Z - otherPlayer.Z;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > ViewDistanceSq)
                continue;

            var entityId = otherPlayer.EntityId;
            currentVisible.Add(entityId);
            if (visibleSet.Add(entityId))
            {
                state.Spawned.Add(otherPlayer);
                continue;
            }

            var key = (observer.SessionId, entityId);
            if (_lastKnown.TryGetValue(key, out var last))
            {
                var positionDelta = Math.Abs(otherPlayer.X - last.X) +
                                    Math.Abs(otherPlayer.Y - last.Y) +
                                    Math.Abs(otherPlayer.Z - last.Z);
                var lookDelta = Math.Abs(otherPlayer.Yaw - last.Yaw) +
                                Math.Abs(otherPlayer.Pitch - last.Pitch);
                if (positionDelta >= MinMoveDelta || lookDelta >= MinLookDelta)
                    state.Moved.Add(new MoveEntry(otherPlayer, last, distSq > TeleportThresholdSq));
            }
            else
            {
                state.Moved.Add(new MoveEntry(otherPlayer, null, IsTeleport: true));
            }
        }

        foreach (var entityId in visibleSet)
        {
            if (!currentVisible.Contains(entityId))
                state.Despawned.Add(entityId);
        }

        foreach (var entityId in state.Despawned)
        {
            visibleSet.Remove(entityId);
            _lastKnown.Remove((observer.SessionId, entityId));
        }

        foreach (var entityId in currentVisible)
        {
            if (playersByEntityId.TryGetValue(entityId, out var player))
            {
                _lastKnown[(observer.SessionId, entityId)] = new LastKnownState(
                    player.X, player.Y, player.Z,
                    player.Yaw, player.Pitch, player.OnGround);
            }
        }

        return new VisibilityResult(state.Spawned, state.Despawned, state.Moved);
    }

    public void RemoveObserver(long sessionId)
    {
        _observers.Remove(sessionId);
        _keyScratch.Clear();
        foreach (var key in _lastKnown.Keys)
        {
            if (key.ObserverId == sessionId)
                _keyScratch.Add(key);
        }
        foreach (var key in _keyScratch)
            _lastKnown.Remove(key);
        _keyScratch.Clear();
    }

    public void RemoveEntity(int entityId)
    {
        foreach (var state in _observers.Values)
            state.Visible.Remove(entityId);

        _keyScratch.Clear();
        foreach (var key in _lastKnown.Keys)
        {
            if (key.EntityId == entityId)
                _keyScratch.Add(key);
        }
        foreach (var key in _keyScratch)
            _lastKnown.Remove(key);
        _keyScratch.Clear();
    }

    private sealed class ObserverState
    {
        public HashSet<int> Visible { get; } = new();
        public HashSet<int> CurrentVisible { get; } = new();
        public List<PlayerContext> Spawned { get; } = new();
        public List<int> Despawned { get; } = new();
        public List<MoveEntry> Moved { get; } = new();

        public void ClearScratch()
        {
            CurrentVisible.Clear();
            Spawned.Clear();
            Despawned.Clear();
            Moved.Clear();
        }
    }

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
        private static readonly VisibilityResult EmptyResult =
            new(new List<PlayerContext>(0), new List<int>(0), new List<MoveEntry>(0));

        public bool IsEmpty => Spawned.Count == 0 && Despawned.Count == 0 && Moved.Count == 0;
        public static VisibilityResult Empty => EmptyResult;
    }
}
