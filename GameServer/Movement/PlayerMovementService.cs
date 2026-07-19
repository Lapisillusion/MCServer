using GameServer.Core.Session;
using GameServer.Network;
using GameServer.Players;
using GameServer.World;

namespace GameServer.Movement;

public readonly record struct PlayerMovementResult(
    double X,
    double Y,
    double Z,
    MovementResolution Resolution)
{
    public bool WasClipped => Resolution.WasClipped;
}

/// <summary>Applies collision-resolved movement to the authoritative player entity state.</summary>
public sealed class PlayerMovementService
{
    private readonly MovementCollisionResolver _resolver;

    public PlayerMovementService(ChunkProvider chunks)
        : this(new MovementCollisionResolver(new WorldCollisionService(chunks)))
    {
    }

    public PlayerMovementService(MovementCollisionResolver resolver)
    {
        _resolver = resolver;
    }

    public PlayerMovementResult MoveTo(
        PlayerContext player,
        double targetX,
        double targetY,
        double targetZ,
        long tickNumber)
    {
        var oldX = player.X;
        var oldY = player.Y;
        var oldZ = player.Z;
        var resolution = _resolver.Resolve(
            player.BoundingBox,
            targetX - oldX,
            targetY - oldY,
            targetZ - oldZ);

        var finalX = (resolution.BoundingBox.MinX + resolution.BoundingBox.MaxX) / 2.0;
        var finalY = resolution.BoundingBox.MinY;
        var finalZ = (resolution.BoundingBox.MinZ + resolution.BoundingBox.MaxZ) / 2.0;

        player.X = finalX;
        player.Y = finalY;
        player.Z = finalZ;
        player.VelocityX = resolution.ResolvedX;
        player.VelocityY = resolution.OnGround ? 0.0 : resolution.ResolvedY;
        player.VelocityZ = resolution.ResolvedZ;
        player.OnGround = resolution.OnGround;
        player.LastClientPositionTick = tickNumber;

        return new PlayerMovementResult(finalX, finalY, finalZ, resolution);
    }

    public PlayerMovementResult MoveBy(
        PlayerContext player,
        double deltaX,
        double deltaY,
        double deltaZ)
        => MoveTo(player, player.X + deltaX, player.Y + deltaY, player.Z + deltaZ,
            player.LastClientPositionTick);

    public bool RefreshGroundState(PlayerContext player)
    {
        player.OnGround = _resolver.IsOnGround(player.BoundingBox);
        if (player.OnGround && player.VelocityY < 0)
            player.VelocityY = 0;
        return player.OnGround;
    }

    public static void EnqueuePositionCorrection(SessionContext session)
    {
        var player = session.Player;
        if (player == null || player.AwaitingTeleportConfirm)
            return;

        player.TeleportId = player.TeleportId == int.MaxValue ? 1 : player.TeleportId + 1;
        player.AwaitingTeleportConfirm = true;
        session.EnqueueOutput(S2CPacketBuilders.BuildPlayerPositionAndLook(
            player.X, player.Y, player.Z,
            player.Yaw, player.Pitch,
            flags: 0,
            teleportId: player.TeleportId));
    }
}
