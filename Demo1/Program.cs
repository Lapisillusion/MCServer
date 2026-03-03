using System.Net;
using System.Net.Sockets;
using System.Text;

namespace McProtoSync;

internal class Program
{
    private static void Main()
    {
        var listener = new TcpListener(IPAddress.Any, 25565);
        listener.Start();
        Console.WriteLine("Listening on 0.0.0.0:25565 ...");

        while (true)
        {
            using var client = listener.AcceptTcpClient();
            client.NoDelay = true;
            Console.WriteLine("Client connected.");

            using var stream = client.GetStream();

            var buffer = new List<byte>(8192);
            var temp = new byte[4096];

            while (true)
            {
                int read;
                try
                {
                    read = stream.Read(temp, 0, temp.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Read error: " + ex.Message);
                    break;
                }

                if (read <= 0)
                {
                    Console.WriteLine("Client disconnected.");
                    break;
                }

                // append to buffer
                for (var i = 0; i < read; i++) buffer.Add(temp[i]);

                // try parse as many packets as possible
                while (true)
                {
                    if (!TryReadVarInt(buffer, 0, out var packetLen, out var lenBytes))
                        break; // not enough bytes to read length yet

                    var totalNeeded = lenBytes + packetLen;
                    if (buffer.Count < totalNeeded)
                        break; // wait for more

                    // extract packet data (without length prefix)
                    var packetData = buffer.GetRange(lenBytes, packetLen).ToArray();
                    buffer.RemoveRange(0, totalNeeded);

                    HandlePacket(packetData);
                }
            }
        }
    }

    private static void HandlePacket(byte[] packetData)
    {
        var reader = new PacketReader(packetData);

        var packetId = reader.ReadVarInt();
        Console.WriteLine($"PacketId=0x{packetId:X2}, Remaining={reader.Remaining}");

        // Handshake state only (ID 0x00)
        if (packetId == 0x00)
        {
            var protocol = reader.ReadVarInt();
            var address = reader.ReadString();
            var port = reader.ReadUShortBE();
            var nextState = reader.ReadVarInt();

            Console.WriteLine(
                $"Handshake: protocol={protocol}, address={address}, port={port}, nextState={nextState} ({(nextState == 1 ? "Status" : nextState == 2 ? "Login" : "Unknown")})");
        }
    }

    // Reads VarInt from buffer at offset, WITHOUT removing bytes.
    // Returns false if buffer doesn't contain enough bytes yet.
    private static bool TryReadVarInt(List<byte> buf, int offset, out int value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;

        var numRead = 0;
        var result = 0;
        byte read;

        do
        {
            if (offset + numRead >= buf.Count) return false;
            read = buf[offset + numRead];

            var val = read & 0b0111_1111;
            result |= val << (7 * numRead);

            numRead++;
            if (numRead > 5) throw new Exception("VarInt too big");
        } while ((read & 0b1000_0000) != 0);

        value = result;
        bytesRead = numRead;
        return true;
    }
}

internal class PacketReader
{
    private readonly byte[] _data;
    private int _pos;

    public PacketReader(byte[] data)
    {
        _data = data;
        _pos = 0;
    }

    public int Remaining => _data.Length - _pos;

    public int ReadVarInt()
    {
        var numRead = 0;
        var result = 0;
        byte read;
        do
        {
            if (_pos >= _data.Length) throw new Exception("VarInt truncated");
            read = _data[_pos++];

            var val = read & 0b0111_1111;
            result |= val << (7 * numRead);

            numRead++;
            if (numRead > 5) throw new Exception("VarInt too big");
        } while ((read & 0b1000_0000) != 0);

        return result;
    }

    public string ReadString()
    {
        var len = ReadVarInt();
        if (len < 0 || len > 32767) throw new Exception("String length invalid");
        if (_pos + len > _data.Length) throw new Exception("String truncated");

        var s = Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return s;
    }

    public ushort ReadUShortBE()
    {
        if (_pos + 2 > _data.Length) throw new Exception("UShort truncated");
        var v = (ushort)((_data[_pos] << 8) | _data[_pos + 1]);
        _pos += 2;
        return v;
    }
}