using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Dimension;
using GameServer.Network;
using GameServer.Players;
using GameServer.Replication;
using GameServer.World;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Application;

/// <summary>
/// Orchestrates the complete player join sequence, extracted from BackendGatewayServer.
/// Dependencies: PlayerManager, SpawnManager, DimensionManager, GameServerOptions.
///
/// Network optimization: all join frames are batched into a single stream write
/// to minimize syscalls and avoid Nagle-delayed fragments.
/// When compression is enabled, a Set Compression frame is prepended to the batch.
/// </summary>
public sealed class PlayerJoinFlow
{
    private readonly PlayerManager _playerManager;
    private readonly SpawnManager _spawnManager;
    private readonly DimensionManager _dimensionManager;
    private readonly GameServerOptions _options;
    private readonly SessionRegistry _sessions;
    private readonly EntityTracker _entityTracker;

    public PlayerJoinFlow(
        PlayerManager playerManager,
        SpawnManager spawnManager,
        DimensionManager dimensionManager,
        GameServerOptions options,
        SessionRegistry sessions,
        EntityTracker entityTracker)
    {
        _playerManager = playerManager;
        _spawnManager = spawnManager;
        _dimensionManager = dimensionManager;
        _options = options;
        _sessions = sessions;
        _entityTracker = entityTracker;
    }

    public async Task ExecuteAsync(SessionContext session, CancellationToken ct)
    {
        // 1. Determine spawn
        var spawn = _spawnManager.GetSpawnInfo();

        // 2. Validate dimension
        var dimDef = _dimensionManager.GetDimension(spawn.Dimension);

        // 3. Create player context with unique EntityId
        var player = _playerManager.CreatePlayer(session.PlayerId);
        player.PlayerName = session.PlayerName;
        player.Dimension = spawn.Dimension;
        player.X = spawn.X;
        player.Y = spawn.Y;
        player.Z = spawn.Z;
        player.Yaw = spawn.Yaw;
        player.Pitch = spawn.Pitch;
        player.OnGround = true; // standing on superflat grass at Y=4.0
        session.Player = player;

        Info("JoinFlow", session.SessionId,
            $"Starting join, playerId={session.PlayerId}, entityId={player.EntityId}, " +
            $"dimension={dimDef.Name}({spawn.Dimension}), " +
            $"pos=({spawn.X:F2},{spawn.Y:F2},{spawn.Z:F2}), " +
            $"yaw={spawn.Yaw:F2}, pitch={spawn.Pitch:F2}, teleportId={player.TeleportId}");

        // 4. Build join frames (6 packets + optional compression)
        var frameList = new List<(string Name, byte[] Data)>();

        if (_options.EnableCompression)
        {
            frameList.Add(("SetCompression", S2CPacketBuilders.BuildSetCompression(256)));
        }

        frameList.Add(("JoinGame", S2CPacketBuilders.BuildJoinGame(player.EntityId, player.Gamemode, player.Dimension,
            difficulty: 2, maxPlayers: 0, levelType: dimDef.LevelType, reducedDebugInfo: false)));
        frameList.Add(("PluginMessage", S2CPacketBuilders.BuildPluginMessage("MC|Brand", "vanilla")));
        frameList.Add(("ServerDifficulty", S2CPacketBuilders.BuildServerDifficulty(difficulty: 2)));
        frameList.Add(("SpawnPosition", S2CPacketBuilders.BuildSpawnPosition(0, 4, 0)));
        frameList.Add(("PlayerAbilities", S2CPacketBuilders.BuildPlayerAbilities(flags: 0, flyingSpeed: 0.05f, walkingSpeed: 0.1f)));
        frameList.Add(("PlayerPosAndLook", S2CPacketBuilders.BuildPlayerPositionAndLook(player.X, player.Y, player.Z,
            player.Yaw, player.Pitch, flags: 0, teleportId: player.TeleportId)));

        // M6: establish the server-authoritative starter hotbar before accepting play input.
        for (var slot = 0; slot < GameServer.Inventory.HotbarInventory.SlotCount; slot++)
        {
            var inventorySlot = (short)(36 + slot); // player inventory window: hotbar occupies slots 36..44
            frameList.Add(($"Hotbar[{slot}]", S2CPacketBuilders.BuildSetSlot(0, inventorySlot, player.Hotbar.GetSlot(slot))));
        }

        var frames = frameList.ToArray();

        // 5. Batch all frames into a single write for network efficiency
        var totalBytes = 0;
        foreach (var (_, data) in frames)
            totalBytes += data.Length;

        var batch = new byte[totalBytes];
        var offset = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var (name, data) = frames[i];
            Buffer.BlockCopy(data, 0, batch, offset, data.Length);
            offset += data.Length;
            Info("JoinFlow", session.SessionId,
                $"  [{i + 1}/{frames.Length}] {name} ({data.Length} bytes)");
        }

        if (session.Stream == null)
            throw new InvalidOperationException("Session stream is not set.");

        await session.Stream.WriteAsync(batch, ct);
        await session.Stream.FlushAsync(ct);

        // 6. Transition to Play
        session.State = GameSessionState.Play;

        Info("JoinFlow", session.SessionId,
            $"Join sequence complete, {frames.Length} frames, {totalBytes} total bytes batched, compression={_options.EnableCompression}");

        // 7. Multiplayer: broadcast PlayerListItem to all players.
        // SpawnPlayer is handled by ReplicationStage (single source of truth for entity visibility).
        var existingPlayers = 0;
        foreach (var (otherSid, other) in _sessions.All)
        {
            if (other.SessionId == session.SessionId) continue;
            if (other.State != GameSessionState.Play || other.Closed || other.Player == null) continue;

            existingPlayers++;
            // Send PlayerListItem(ADD) for new player to existing player
            var itemAdd = S2CPacketBuilders.BuildPlayerListItemAdd(
                player.PlayerId, player.PlayerName, gamemode: 0, ping: 0);
            other.EnqueueOutput(itemAdd);
        }

        // 8. Multiplayer: send existing players' PlayerListItem to the new player
        foreach (var (otherSid, other) in _sessions.All)
        {
            if (other.SessionId == session.SessionId) continue;
            if (other.State != GameSessionState.Play || other.Closed || other.Player == null) continue;

            var otherP = other.Player;
            var itemAdd = S2CPacketBuilders.BuildPlayerListItemAdd(
                otherP.PlayerId, otherP.PlayerName, gamemode: 0, ping: 0);
            session.EnqueueOutput(itemAdd);
        }

        if (existingPlayers > 0)
            Info("JoinFlow", session.SessionId,
                $"Multiplayer broadcast: {existingPlayers} existing players notified via PlayerListItem");
    }
}
