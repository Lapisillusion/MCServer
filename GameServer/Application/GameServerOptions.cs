using System.Net;

namespace GameServer.Application;

public sealed record GameServerOptions(
    IPEndPoint GatewayBackendListenEndPoint,
    bool EnableCompression = false,
    string PlayerDataDirectory = "data/players")
{
    public static GameServerOptions CreateDefault()
    {
        return new GameServerOptions(
            GatewayBackendListenEndPoint: new IPEndPoint(IPAddress.Loopback, 25566),
            EnableCompression: false,
            PlayerDataDirectory: "data/players");
    }
}
