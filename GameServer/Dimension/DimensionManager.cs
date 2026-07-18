using static GameServer.Core.Diagnostics.GameLogger;

namespace GameServer.Dimension;

/// <summary>
/// Registry of available dimensions with their protocol-level properties.
/// </summary>
public sealed class DimensionManager
{
    private readonly Dictionary<int, DimensionDefinition> _dimensions = new();

    public void Register(DimensionDefinition definition)
    {
        if (!_dimensions.TryAdd(definition.Id, definition))
            throw new InvalidOperationException($"Duplicate dimension id: {definition.Id}");
    }

    public DimensionDefinition GetDimension(int dimensionId)
    {
        if (!_dimensions.TryGetValue(dimensionId, out var definition))
            throw new KeyNotFoundException($"Unknown dimension id: {dimensionId}");
        return definition;
    }

    public bool TryGetDimension(int dimensionId, out DimensionDefinition definition)
        => _dimensions.TryGetValue(dimensionId, out definition);

    public void RegisterDefaults()
    {
        Register(new DimensionDefinition(0, "overworld", "flat"));
        Register(new DimensionDefinition(-1, "the_nether", "default"));
        Register(new DimensionDefinition(1, "the_end", "default"));
        Info("Dimension", "Default dimensions registered (overworld, nether, end)");
    }
}

/// <summary>Immutable dimension descriptor.</summary>
public readonly record struct DimensionDefinition(int Id, string Name, string LevelType);
