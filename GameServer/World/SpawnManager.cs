namespace GameServer.World;

/// <summary>
/// Determines spawn position and dimension for new players.
/// Currently returns a fixed superflat spawn; configurable later.
/// </summary>
public sealed class SpawnManager
{
    private SpawnInfo _spawn = new(0, 0.5, 4.0, 0.5, 0f, 0f);

    public SpawnInfo GetSpawnInfo() => _spawn;

    public void SetSpawn(SpawnInfo spawn) => _spawn = spawn;
}

/// <summary>Immutable spawn coordinates for player initialization.</summary>
public readonly record struct SpawnInfo(
    int Dimension,
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch);
