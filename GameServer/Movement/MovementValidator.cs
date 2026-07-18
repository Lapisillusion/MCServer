namespace GameServer.Movement;

/// <summary>Pure validation functions — no side effects.</summary>
public static class MovementValidator
{
    /// <summary>Validate position+look. Returns null on success, or rejection reason.</summary>
    public static string? ValidatePositionAndLook(
        double newX, double newY, double newZ,
        float newYaw, float newPitch,
        double oldX, double oldY, double oldZ,
        float oldYaw, float oldPitch,
        MovementConfig config)
    {
        var posError = ValidatePosition(newX, newY, newZ, oldX, oldY, oldZ, config);
        if (posError != null) return posError;

        return ValidateLook(newYaw, newPitch);
    }

    /// <summary>Validate position only.</summary>
    public static string? ValidatePosition(
        double newX, double newY, double newZ,
        double oldX, double oldY, double oldZ,
        MovementConfig config)
    {
        // NaN / Infinity
        if (double.IsNaN(newX) || double.IsInfinity(newX) ||
            double.IsNaN(newY) || double.IsInfinity(newY) ||
            double.IsNaN(newZ) || double.IsInfinity(newZ))
            return "Position contains NaN or Infinity";

        // Y bounds
        if (newY < config.MinY || newY > config.MaxY)
            return $"Y={newY:F1} out of bounds [{config.MinY}, {config.MaxY}]";

        // Max move distance
        var dx = newX - oldX;
        var dy = newY - oldY;
        var dz = newZ - oldZ;
        var distSq = dx * dx + dy * dy + dz * dz;
        if (distSq > config.MaxMoveDistancePerTick * config.MaxMoveDistancePerTick)
            return $"Move distance too large: {Math.Sqrt(distSq):F1}";

        // Vertical speed anomaly
        if (Math.Abs(dy) > config.MaxVerticalSpeed)
            return $"Vertical speed anomaly: {Math.Abs(dy):F1}";

        // Horizontal speed anomaly
        var hDist = Math.Sqrt(dx * dx + dz * dz);
        if (hDist > config.MaxHorizontalSpeedPerTick)
            return $"Horizontal speed anomaly: {hDist:F1}";

        return null;
    }

    /// <summary>Validate rotation only.</summary>
    public static string? ValidateLook(float newYaw, float newPitch)
    {
        if (float.IsNaN(newYaw) || float.IsInfinity(newYaw))
            return "Yaw is NaN or Infinity";
        if (float.IsNaN(newPitch) || float.IsInfinity(newPitch))
            return "Pitch is NaN or Infinity";
        return null;
    }
}
