using System.Net;
using System.Net.Sockets;

const int port = 25566;

var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"Echo server started on 127.0.0.1:{port}");

while (true)
{
    using var client = listener.AcceptTcpClient();
    Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

    using var stream = client.GetStream();
    var buffer = new byte[1024];
    int bytesRead;

    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
    {
        stream.Write(buffer, 0, bytesRead);
    }

    Console.WriteLine("Client disconnected");
}
