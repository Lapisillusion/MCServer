// ConnectionContext.cs

using System.Net.Sockets;

namespace GateWay;

public sealed class ConnectionContext
{
    public readonly ByteRingBuffer Inbound = new();
    public Socket Backend = null!;

    public Socket Client = null!;

    // 简化：发送直接用 Socket.SendAsync(SAEA)；MVP 先不做复杂发送队列
    public volatile bool Closed;
    public uint IpV4;
    public ConnState State = ConnState.Handshake;

    public void Close()
    {
        if (Closed) return;
        Closed = true;
        try
        {
            Client.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            Backend.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            Client.Close();
        }
        catch
        {
        }

        try
        {
            Backend.Close();
        }
        catch
        {
        }
    }
}