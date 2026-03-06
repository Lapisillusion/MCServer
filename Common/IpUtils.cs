// IpUtils.cs

using System.Net;

namespace Common;

public static class IpUtils
{
    public static uint ToUInt32(IPAddress ipv4)
    {
        var b = ipv4.GetAddressBytes(); // 4 bytes
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}
