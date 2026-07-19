using System.Net;

namespace GameServer.Application;

public sealed record GameServerOptions(
    IPEndPoint GatewayBackendListenEndPoint,
    bool EnableCompression,
    string PlayerDataDirectory,
    TimeSpan KeepAliveInitialDelay,
    TimeSpan KeepAliveInterval,
    TimeSpan KeepAliveTimeout,
    int DefaultChunkViewRadius,
    int MaxChunkViewRadius)
{
    /// <summary>Hard cap applied independently to every player during InputCollect.</summary>
    public int MaxInputFramesPerTick { get; init; } = 64;

    /// <summary>Maximum new chunk columns generated/queued for one player in one tick.</summary>
    public int MaxChunkLoadsPerTick { get; init; } = 4;

    /// <summary>Maximum bytes moved from a player's packet queue into the async send queue per tick.</summary>
    public int MaxNetworkBatchBytes { get; init; } = 64 * 1024;

    /// <summary>Maximum combined queued and in-flight output bytes allowed for one player.</summary>
    public int MaxPendingSendBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Number of recent ticks retained for percentile calculation.</summary>
    public int TickMetricsWindowSize { get; init; } = 1200;

    public static GameServerOptions CreateDefault()
    {
        return new GameServerOptions(
            GatewayBackendListenEndPoint: new IPEndPoint(IPAddress.Loopback, 25566),
            EnableCompression: false,
            PlayerDataDirectory: "data/players",
            KeepAliveInitialDelay: TimeSpan.FromSeconds(2),
            KeepAliveInterval: TimeSpan.FromSeconds(10),
            KeepAliveTimeout: TimeSpan.FromSeconds(30),
            DefaultChunkViewRadius: 1,
            MaxChunkViewRadius: 4);
    }
}
