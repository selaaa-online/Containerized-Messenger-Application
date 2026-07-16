using System.Net;
using System.Net.Sockets;

namespace Shared.Tests;

/// <summary>
/// Helper that creates a connected pair of <see cref="TcpClient"/> instances over
/// the loopback interface so socket-backed types can be exercised end to end.
/// </summary>
internal static class Loopback
{
    public static async Task<(TcpClient client, TcpClient server)> CreatePairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var connectTask = ConnectAsync(port);
            var server = await listener.AcceptTcpClientAsync();
            var client = await connectTask;
            return (client, server);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        return client;
    }
}
