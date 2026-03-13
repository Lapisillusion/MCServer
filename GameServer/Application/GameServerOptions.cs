using System.Net;

namespace GameServer.Application;

public sealed record GameServerOptions(
    IPEndPoint GatewayBackendListenEndPoint)
{
    public static GameServerOptions CreateDefault()
    {
        return new GameServerOptions(
            GatewayBackendListenEndPoint: new IPEndPoint(IPAddress.Loopback, 25566));
    }
}
