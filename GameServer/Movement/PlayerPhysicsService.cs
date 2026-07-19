using GameServer.Core.Session;
using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Movement;

/// <summary>
/// Minimal vertical fallback simulation. Client position packets remain predicted locally,
/// while the server applies gravity only when no position update arrived for the current tick.
/// </summary>
public sealed class PlayerPhysicsService
{
    public const double GravityPerTick = 0.08;
    public const double VerticalDrag = 0.98;
    public const double TerminalVelocity = -3.92;

    private readonly PlayerMovementService _movement;

    public PlayerPhysicsService(PlayerMovementService movement)
    {
        _movement = movement;
    }

    public void Update(SessionContext session, long tickNumber)
    {
        var player = session.Player;
        if (session.Closed || session.CloseRequested ||
            session.State != GameServer.Core.Dispatch.GameSessionState.Play ||
            player == null || !player.ChunksSent || player.Removed ||
            player.AwaitingTeleportConfirm)
            return;

        var onGround = _movement.RefreshGroundState(player);
        if (onGround)
        {
            player.VelocityY = 0;
            return;
        }

        if (player.LastClientPositionTick == tickNumber)
            return;

        player.VelocityY = Math.Max(
            (player.VelocityY - GravityPerTick) * VerticalDrag,
            TerminalVelocity);

        var oldY = player.Y;
        var result = _movement.MoveBy(player, 0, player.VelocityY, 0);
        if (result.Resolution.CollidedVertically)
            player.VelocityY = 0;

        if (Math.Abs(player.Y - oldY) <= AxisAlignedBox.CollisionEpsilon ||
            player.AwaitingTeleportConfirm)
            return;

        PlayerMovementService.EnqueuePositionCorrection(session);
        Info("Physics", session.SessionId, session.PlayerName,
            $"Applied gravity, y={player.Y:F3}, velocityY={player.VelocityY:F3}, onGround={player.OnGround}");
    }
}
