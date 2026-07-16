using System.Net.Sockets;
using System.Text;
using Shared;
using Xunit;

namespace Shared.Tests;

public class MessageChannelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SendAsync_ThenReadAsync_RoundTripsMessage()
    {
        var (client, server) = await Loopback.CreatePairAsync();
        using (client)
        using (server)
        {
            using var sender = new MessageChannel(client.GetStream());
            using var receiver = new MessageChannel(server.GetStream());

            await sender.SendAsync(Message.Chat("alice", "hello"));
            var received = await ReadWithTimeout(receiver);

            Assert.NotNull(received);
            Assert.Equal(MessageType.Chat, received!.Type);
            Assert.Equal("alice", received.Username);
            Assert.Equal("hello", received.Text);
        }
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_WhenPeerClosesConnection()
    {
        var (client, server) = await Loopback.CreatePairAsync();
        using (server)
        {
            using var receiver = new MessageChannel(server.GetStream());

            // Close the remote peer so the read observes end-of-stream.
            client.Close();

            var received = await ReadWithTimeout(receiver);

            Assert.Null(received);
        }
    }

    [Fact]
    public async Task ReadAsync_SkipsEmptyFrames()
    {
        var (client, server) = await Loopback.CreatePairAsync();
        using (client)
        using (server)
        {
            using var receiver = new MessageChannel(server.GetStream());

            // Write a stray empty line before a real message frame.
            var raw = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
            await raw.WriteAsync("\n");
            await raw.WriteAsync(Message.Chat("alice", "hi").ToJson() + "\n");

            var received = await ReadWithTimeout(receiver);

            Assert.NotNull(received);
            Assert.Equal("hi", received!.Text);
        }
    }

    [Fact]
    public async Task SendAsync_SupportsConcurrentWriters()
    {
        var (client, server) = await Loopback.CreatePairAsync();
        using (client)
        using (server)
        {
            using var sender = new MessageChannel(client.GetStream());
            using var receiver = new MessageChannel(server.GetStream());

            const int count = 20;
            var sends = Enumerable.Range(0, count)
                .Select(i => sender.SendAsync(Message.Chat("alice", i.ToString())));
            await Task.WhenAll(sends);

            var texts = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var msg = await ReadWithTimeout(receiver);
                Assert.NotNull(msg);
                texts.Add(msg!.Text!);
            }

            // Every message must arrive intact (order is not guaranteed across writers).
            Assert.Equal(
                Enumerable.Range(0, count).Select(i => i.ToString()).OrderBy(x => x),
                texts.OrderBy(x => x));
        }
    }

    private static async Task<Message?> ReadWithTimeout(MessageChannel channel)
    {
        using var cts = new CancellationTokenSource(Timeout);
        return await channel.ReadAsync(cts.Token);
    }
}
