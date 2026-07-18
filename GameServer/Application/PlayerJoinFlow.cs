using GameServer.Core.Dispatch;
using GameServer.Core.Session;
using GameServer.Dimension;
using GameServer.Network;
using GameServer.Players;
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

    public PlayerJoinFlow(
        PlayerManager playerManager,
        SpawnManager spawnManager,
        DimensionManager dimensionManager,
        GameServerOptions options)
    {
        _playerManager = playerManager;
        _spawnManager = spawnManager;
        _dimensionManager = dimensionManager;
        _options = options;
    }

    public async Task ExecuteAsync(SessionContext session, CancellationToken ct)
    {
        // 1. Determine spawn
        var spawn = _spawnManager.GetSpawnInfo();

        // 2. Validate dimension
        var dimDef = _dimensionManager.GetDimension(spawn.Dimension);

        // 3. Create player context with unique EntityId
        var player = _playerManager.CreatePlayer(session.PlayerId);
        player.Dimension = spawn.Dimension;
        player.X = spawn.X;
        player.Y = spawn.Y;
        player.Z = spawn.Z;
        player.Yaw = spawn.Yaw;
        player.Pitch = spawn.Pitch;
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
        frameList.Add(("PlayerAbilities", S2CPacketBuilders.BuildPlayerAbilities(flags: 0, flyingSpeed: 0.05f, walkingSpeed: 0.1f)));
        frameList.Add(("SpawnPosition", S2CPacketBuilders.BuildSpawnPosition(0, 4, 0)));
        frameList.Add(("PlayerPosAndLook", S2CPacketBuilders.BuildPlayerPositionAndLook(player.X, player.Y, player.Z,
            player.Yaw, player.Pitch, flags: 0, teleportId: player.TeleportId)));

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
    }
}
