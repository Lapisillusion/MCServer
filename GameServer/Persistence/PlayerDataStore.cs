using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameServer.Inventory;
using Serilog;

namespace GameServer.Persistence;

/// <summary>Persistent state deliberately limited to the M7 player baseline.</summary>
public sealed record PlayerSaveData(
    string PlayerId,
    int Dimension,
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch,
    bool OnGround,
    int SelectedHotbarSlot,
    ItemStackSnapshot?[]? Hotbar);

public interface IPlayerDataStore
{
    Task<PlayerSaveData?> LoadAsync(string playerId, CancellationToken cancellationToken = default);
    Task SaveAsync(PlayerSaveData data, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON-backed player store. File names are SHA-256 player-ID hashes so externally supplied
/// IDs cannot influence the storage path.
/// </summary>
public sealed class FilePlayerDataStore : IPlayerDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directoryPath;

    public FilePlayerDataStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
    }

    public async Task<PlayerSaveData?> LoadAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var path = GetFilePath(playerId);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PlayerSaveData>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Ignoring corrupt player data file {PlayerDataPath}", path);
            return null;
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Could not read player data file {PlayerDataPath}", path);
            return null;
        }
    }

    public async Task SaveAsync(PlayerSaveData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var path = GetFilePath(data.PlayerId);
        Directory.CreateDirectory(_directoryPath);

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public string GetFilePath(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(playerId));
        return Path.Combine(_directoryPath, Convert.ToHexString(hash) + ".json");
    }
}
