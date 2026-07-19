using GameServer.Tick;

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

/// <summary>Vanilla-style Y → X → Z clipping with a reusable obstacle buffer.</summary>
public sealed class MovementCollisionResolver
{
    private const double GroundProbeDistance = 1.0E-3;
    private readonly WorldCollisionService _world;
    private readonly TickMetrics? _metrics;
    private readonly List<AxisAlignedBox> _obstacleScratch = new(32);

    public MovementCollisionResolver(WorldCollisionService world, TickMetrics? metrics = null)
    {
        _world = world;
        _metrics = metrics;
    }

    public MovementResolution Resolve(
        in AxisAlignedBox original,
        double requestedX,
        double requestedY,
        double requestedZ)
    {
        _obstacleScratch.Clear();
        _world.AppendCollisionBoxes(
            original.ExpandTowards(requestedX, requestedY, requestedZ), _obstacleScratch);
        _metrics?.AddCollisionQueries();

        var box = original;
        var resolvedY = requestedY;
        foreach (var obstacle in _obstacleScratch)
            resolvedY = obstacle.ClipYOffset(box, resolvedY);
        box = box.Offset(0, resolvedY, 0);

        var resolvedX = requestedX;
        foreach (var obstacle in _obstacleScratch)
            resolvedX = obstacle.ClipXOffset(box, resolvedX);
        box = box.Offset(resolvedX, 0, 0);

        var resolvedZ = requestedZ;
        foreach (var obstacle in _obstacleScratch)
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
    {
        _metrics?.AddCollisionQueries();
        return _world.HasCollision(box.Offset(0, -GroundProbeDistance, 0));
    }
}
