// Blacklist.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Common;

namespace GateWay;

public sealed class Blacklist
{
    private readonly ConcurrentDictionary<uint, long> _banUntilTicks = new();
    private readonly List<(uint Net, uint Mask)> _cidrs = new();
    private readonly HashSet<uint> _ipSet = new();
    private readonly double _ticksPerSec = Stopwatch.Frequency;

    public void AddIp(string ip)
    {
        if (IPAddress.TryParse(ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
            _ipSet.Add(IpUtils.ToUInt32(addr));
    }

    public void AddCidr(string cidr)
    {
        // e.g. "1.2.3.0/24"
        var parts = cidr.Split('/');
        if (parts.Length != 2) return;
        if (!IPAddress.TryParse(parts[0], out var ip)) return;
        if (!int.TryParse(parts[1], out var bits)) return;
        if (ip.AddressFamily != AddressFamily.InterNetwork) return;
        bits = Math.Clamp(bits, 0, 32);

        var mask = bits == 0 ? 0u : 0xFFFF_FFFFu << (32 - bits);
        var net = IpUtils.ToUInt32(ip) & mask;
        _cidrs.Add((net, mask));
    }

    public bool IsBlocked(uint ipV4)
    {
        var now = Stopwatch.GetTimestamp();

        // 动态临封
        if (_banUntilTicks.TryGetValue(ipV4, out var until) && until > now)
            return true;

        // 静态 IP
        if (_ipSet.Contains(ipV4))
            return true;

        // CIDR
        foreach (var (net, mask) in _cidrs)
            if ((ipV4 & mask) == net)
                return true;

        return false;
    }

    public void TempBan(uint ipV4, TimeSpan duration)
    {
        var now = Stopwatch.GetTimestamp();
        var until = now + (long)(_ticksPerSec * duration.TotalSeconds);
        _banUntilTicks[ipV4] = until;
    }
}
