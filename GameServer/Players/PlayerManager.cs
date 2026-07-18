using System.Collections.Concurrent;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Players;

/// <summary>
/// Central registry for PlayerContext instances. Assigns unique EntityIds.
/// Thread-safe via ConcurrentDictionary and Interlocked.
/// </summary>
public sealed class PlayerManager
{
    private readonly ConcurrentDictionary<int, PlayerContext> _players = new();
    private int _nextEntityId;

    public int Count => _players.Count;

    public PlayerContext CreatePlayer(string playerId)
    {
        var entityId = Interlocked.Increment(ref _nextEntityId);
        var player = new PlayerContext
        {
            EntityId = entityId,
            PlayerId = playerId,
            Gamemode = 0,
            Dimension = 0,
            X = 0.5,
            Y = 4.0,
            Z = 0.5,
            Yaw = 0f,
            Pitch = 0f,
            TeleportId = 1
        };

        if (!_players.TryAdd(entityId, player))
            throw new InvalidOperationException($"EntityId collision: {entityId}");

        Info("PlayerManager", 0, playerId, "",
            $"Player created, entityId={entityId}, total players={_players.Count}");

        return player;
    }

    public bool TryGetPlayer(int entityId, out PlayerContext player)
        => _players.TryGetValue(entityId, out player!);

    public bool RemovePlayer(int entityId, out PlayerContext? player)
    {
        if (!_players.TryRemove(entityId, out player))
            return false;

        Info("PlayerManager", 0, player.PlayerId, "",
            $"Player removed, entityId={entityId}, total players={_players.Count}");
        return true;
    }
}
