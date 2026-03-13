using System.Net;

namespace GateWay;

public sealed record GateWayOptions(
    IPEndPoint ListenEndPoint,
    IPEndPoint GameServerEndPoint)
{
    public static GateWayOptions CreateDefault()
    {
        return new GateWayOptions(
            ListenEndPoint: new IPEndPoint(IPAddress.Any, 25565),
            GameServerEndPoint: new IPEndPoint(IPAddress.Loopback, 25566));
    }
}
