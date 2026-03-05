using System.Net;
using GateWay;

public class GateWayStarter
{
    public static void Main()
    {
        var listen = new IPEndPoint(IPAddress.Any, 25565);
        var backend = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 25566);

        var blacklist = new Blacklist();
        // blacklist.AddIp("1.2.3.4");
        // blacklist.AddCidr("5.6.7.0/24");

        var limiter = new RateLimiter();

        var server = new GateWayServer(listen, backend, blacklist, limiter);
        server.Start();

        Console.WriteLine("Press Enter to quit.");
        Console.ReadLine();
    }
}