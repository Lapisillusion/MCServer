namespace GameServer.Movement;

/// <summary>Validation thresholds. Struct avoids heap allocation.</summary>
public readonly record struct MovementConfig(
    double MaxMoveDistancePerTick,
    double MinY,
    double MaxY,
    double MaxVerticalSpeed,
    double MaxHorizontalSpeedPerTick)
{
    public static MovementConfig Default => new(
        MaxMoveDistancePerTick: 100.0,
        MinY: -64.0,
        MaxY: 320.0,
        MaxVerticalSpeed: 50.0,
        MaxHorizontalSpeedPerTick: 30.0);
}
