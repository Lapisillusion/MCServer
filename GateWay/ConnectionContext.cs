// ConnectionContext.cs

using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace GateWay;

public enum IoOperation : byte
{
    Accept,
    ClientRecv,
    BackendRecv,
    BackendSend,
    ClientSend
}

public sealed class IoToken
{
    public required IoOperation Operation;
    public ConnectionContext? Context;
}

public sealed class SendWorkItem
{
    public required ArraySegment<byte> Segment1;
    public ArraySegment<byte> Segment2;
    public required int TotalLength;
    public byte[]? Rented;
    public bool FromClientInboundRing;
    public int SegmentIndex;
    public int SegmentOffset;
}

public sealed class SendChannel
{
    public readonly Queue<SendWorkItem> Queue = new();
    public readonly object Sync = new();
    public SendWorkItem? Current;
    public bool Sending;
}

public sealed class ConnectionContext
{
    private int _closed;

    public readonly ByteRingBuffer ClientInbound = new();
    public readonly SendChannel BackendSend = new();
    public readonly SendChannel ClientSend = new();
    public readonly object ParserSync = new();

    public Socket Backend = null!;
    public Socket Client = null!;

    public SocketAsyncEventArgs BackendRecvSaea = null!;
    public SocketAsyncEventArgs BackendSendSaea = null!;
    public SocketAsyncEventArgs ClientRecvSaea = null!;
    public SocketAsyncEventArgs ClientSendSaea = null!;

    public long LastActivityTicks;
    public int Id;
    public int ParsedClientBytes;
    public bool LoginSlotHeld;
    public uint IpV4;
    public int InvalidPacketCount;
    public ConnState State = ConnState.Handshake;

    public bool Closed => Volatile.Read(ref _closed) != 0;

    public bool TryMarkClosed()
    {
        return Interlocked.CompareExchange(ref _closed, 1, 0) == 0;
    }
}
