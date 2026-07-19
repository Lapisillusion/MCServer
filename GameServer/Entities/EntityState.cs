using GameServer.Movement;

namespace GameServer.Entities;

/// <summary>Shared server-authoritative spatial state for players and future entities.</summary>
public abstract class EntityState
{
    public int EntityId { get; set; }
    public int Dimension { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double VelocityZ { get; set; }
    public bool OnGround { get; set; }
    public bool Removed { get; set; }

    public abstract double Width { get; }
    public abstract double Height { get; }

    public AxisAlignedBox BoundingBox
        => AxisAlignedBox.FromFeetPosition(X, Y, Z, Width, Height);
}
