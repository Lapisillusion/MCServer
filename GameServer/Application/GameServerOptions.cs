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
