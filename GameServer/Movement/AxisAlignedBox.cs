namespace GameServer.Movement;

/// <summary>Immutable axis-aligned bounding box used by entity and block collision code.</summary>
public readonly record struct AxisAlignedBox(
    double MinX,
    double MinY,
    double MinZ,
    double MaxX,
    double MaxY,
    double MaxZ)
{
    public const double CollisionEpsilon = 1.0E-7;

    public static AxisAlignedBox FromFeetPosition(
        double x, double y, double z, double width, double height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var halfWidth = width / 2.0;
        return new AxisAlignedBox(
            x - halfWidth, y, z - halfWidth,
            x + halfWidth, y + height, z + halfWidth);
    }

    public AxisAlignedBox Offset(double x, double y, double z)
        => new(MinX + x, MinY + y, MinZ + z, MaxX + x, MaxY + y, MaxZ + z);

    public AxisAlignedBox ExpandTowards(double x, double y, double z)
        => new(
            x < 0 ? MinX + x : MinX,
            y < 0 ? MinY + y : MinY,
            z < 0 ? MinZ + z : MinZ,
            x > 0 ? MaxX + x : MaxX,
            y > 0 ? MaxY + y : MaxY,
            z > 0 ? MaxZ + z : MaxZ);

    public bool Intersects(in AxisAlignedBox other)
        => MaxX > other.MinX && MinX < other.MaxX &&
           MaxY > other.MinY && MinY < other.MaxY &&
           MaxZ > other.MinZ && MinZ < other.MaxZ;

    public double ClipXOffset(in AxisAlignedBox moving, double offset)
    {
        if (moving.MaxY <= MinY || moving.MinY >= MaxY ||
            moving.MaxZ <= MinZ || moving.MinZ >= MaxZ)
            return offset;

        if (offset > 0 && moving.MaxX <= MinX)
            offset = Math.Min(offset, MinX - moving.MaxX);
        else if (offset < 0 && moving.MinX >= MaxX)
            offset = Math.Max(offset, MaxX - moving.MinX);

        return offset;
    }

    public double ClipYOffset(in AxisAlignedBox moving, double offset)
    {
        if (moving.MaxX <= MinX || moving.MinX >= MaxX ||
            moving.MaxZ <= MinZ || moving.MinZ >= MaxZ)
            return offset;

        if (offset > 0 && moving.MaxY <= MinY)
            offset = Math.Min(offset, MinY - moving.MaxY);
        else if (offset < 0 && moving.MinY >= MaxY)
            offset = Math.Max(offset, MaxY - moving.MinY);

        return offset;
    }

    public double ClipZOffset(in AxisAlignedBox moving, double offset)
    {
        if (moving.MaxX <= MinX || moving.MinX >= MaxX ||
            moving.MaxY <= MinY || moving.MinY >= MaxY)
            return offset;

        if (offset > 0 && moving.MaxZ <= MinZ)
            offset = Math.Min(offset, MinZ - moving.MaxZ);
        else if (offset < 0 && moving.MinZ >= MaxZ)
            offset = Math.Max(offset, MaxZ - moving.MinZ);

        return offset;
    }
}
