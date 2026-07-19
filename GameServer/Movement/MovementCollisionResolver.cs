namespace GameServer.Movement;

public readonly record struct MovementResolution(
    AxisAlignedBox BoundingBox,
    double RequestedX,
    double RequestedY,
    double RequestedZ,
    double ResolvedX,
    double ResolvedY,
    double ResolvedZ,
    bool OnGround)
{
    public bool CollidedHorizontally =>
        Math.Abs(RequestedX - ResolvedX) > AxisAlignedBox.CollisionEpsilon ||
        Math.Abs(RequestedZ - ResolvedZ) > AxisAlignedBox.CollisionEpsilon;

    public bool CollidedVertically =>
        Math.Abs(RequestedY - ResolvedY) > AxisAlignedBox.CollisionEpsilon;

    public bool WasClipped => CollidedHorizontally || CollidedVertically;
}

/// <summary>Vanilla-style Y → X → Z axis clipping against block collision boxes.</summary>
public sealed class MovementCollisionResolver
{
    private const double GroundProbeDistance = 1.0E-3;
    private readonly WorldCollisionService _world;

    public MovementCollisionResolver(WorldCollisionService world)
    {
        _world = world;
    }

    public MovementResolution Resolve(
        in AxisAlignedBox original,
        double requestedX,
        double requestedY,
        double requestedZ)
    {
        var obstacles = _world.GetCollisionBoxes(
            original.ExpandTowards(requestedX, requestedY, requestedZ));

        var box = original;
        var resolvedY = requestedY;
        foreach (var obstacle in obstacles)
            resolvedY = obstacle.ClipYOffset(box, resolvedY);
        box = box.Offset(0, resolvedY, 0);

        var resolvedX = requestedX;
        foreach (var obstacle in obstacles)
            resolvedX = obstacle.ClipXOffset(box, resolvedX);
        box = box.Offset(resolvedX, 0, 0);

        var resolvedZ = requestedZ;
        foreach (var obstacle in obstacles)
            resolvedZ = obstacle.ClipZOffset(box, resolvedZ);
        box = box.Offset(0, 0, resolvedZ);

        var landed = requestedY < 0 &&
                     Math.Abs(requestedY - resolvedY) > AxisAlignedBox.CollisionEpsilon;
        var supported = _world.HasCollision(box.Offset(0, -GroundProbeDistance, 0));

        return new MovementResolution(
            box,
            requestedX, requestedY, requestedZ,
            resolvedX, resolvedY, resolvedZ,
            landed || supported);
    }

    public bool IsOnGround(in AxisAlignedBox box)
        => _world.HasCollision(box.Offset(0, -GroundProbeDistance, 0));
}
