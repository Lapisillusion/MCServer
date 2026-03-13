using GateWay;
using Serilog;
using Serilog.Events;

public class GateWayStarter
{
    /// <summary>
    /// 网关主入口
    /// </summary>
    public static void Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/gateway-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        try
        {
            var options = GateWayOptions.CreateDefault();

            var blacklist = new Blacklist();
            // blacklist.AddIp("1.2.3.4");
            // blacklist.AddCidr("5.6.7.0/24");

            var limiter = new RateLimiter();

            Log.Information("Gateway boot options: listen={Listen}, gameServer={GameServer}",
                options.ListenEndPoint, options.GameServerEndPoint);

            var server = new GateWayServer(options, blacklist, limiter);
            server.Start();

            Log.Information("Press Enter to quit.");
            Console.ReadLine();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Gateway terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
