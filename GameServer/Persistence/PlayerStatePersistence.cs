using GameServer.Dimension;
using GameServer.Movement;
using GameServer.Players;

namespace GameServer.Persistence;

/// <summary>Maps runtime player state to validated persistent state.</summary>
public static class PlayerStatePersistence
{
    public static PlayerSaveData Capture(PlayerContext player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new PlayerSaveData(
            player.PlayerId,
            player.Dimension,
            player.X,
            player.Y,
            player.Z,
            player.Yaw,
            player.Pitch,
            player.OnGround,
            player.Hotbar.SelectedSlot,
            player.Hotbar.Snapshot());
    }

    /// <summary>Applies only fully valid persisted data; the caller keeps default spawn state on failure.</summary>
    public static bool TryApply(
        PlayerContext player,
        PlayerSaveData saved,
        DimensionManager dimensions,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(dimensions);

        if (!string.Equals(player.PlayerId, saved.PlayerId, StringComparison.Ordinal))
        {
            error = "Player ID does not match the save record";
            return false;
        }

        if (!dimensions.TryGetDimension(saved.Dimension, out _))
        {
            error = $"Unknown dimension: {saved.Dimension}";
            return false;
        }

        var movement = MovementConfig.Default;
        if (!double.IsFinite(saved.X) || !double.IsFinite(saved.Y) || !double.IsFinite(saved.Z) ||
            saved.Y < movement.MinY || saved.Y > movement.MaxY ||
            !float.IsFinite(saved.Yaw) || !float.IsFinite(saved.Pitch))
        {
            error = "Saved position or rotation is invalid";
            return false;
        }

        if (!player.Hotbar.TryRestore(saved.SelectedHotbarSlot, saved.Hotbar))
        {
            error = "Saved hotbar is invalid";
            return false;
        }

        player.Dimension = saved.Dimension;
        player.X = saved.X;
        player.Y = saved.Y;
        player.Z = saved.Z;
        player.Yaw = saved.Yaw;
        player.Pitch = saved.Pitch;
        player.OnGround = saved.OnGround;
        error = string.Empty;
        return true;
    }
}
