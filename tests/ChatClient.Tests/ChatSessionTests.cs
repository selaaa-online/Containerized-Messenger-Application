using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using ChatClient;
using Shared;
using Xunit;

namespace ChatClient.Tests;

/// <summary>
/// Loopback tests that drive a real <see cref="ChatSession"/> against a minimal fake
/// server, verifying the authentication handshake, slash-command parsing on the send
/// path, and inbound message rendering on the receive path.
/// </summary>
public class ChatSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAsync_AuthenticatesAndSendsParsedCommands()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Fake server: authenticate, then capture the next three inbound messages.
        var serverTask = Task.Run(async () =>
        {
            using var srv = await listener.AcceptTcpClientAsync();
            using var channel = new MessageChannel(srv.GetStream());

            var auth = await channel.ReadAsync();
            await channel.SendAsync(Message.AuthResult(true, "welcome"));

            var received = new List<Message>();
            for (var i = 0; i < 3; i++)
            {
                var msg = await channel.ReadAsync();
                if (msg is null)
                {
                    break;
                }

                received.Add(msg);
            }

            return (auth, received);
        });

        var outgoing = Channel.CreateUnbounded<string>();
        outgoing.Writer.TryWrite("hello world");
        outgoing.Writer.TryWrite("/list");
        outgoing.Writer.TryWrite("/msg bob hey there");
        outgoing.Writer.TryWrite("/quit");

        var session = new ChatSession("127.0.0.1", port, "alice", "pw");
        using var cts = new CancellationTokenSource(Timeout);

        var reason = await session.RunAsync(outgoing.Reader, cts.Token);

        Assert.Equal(ChatSession.SessionEndReason.UserQuit, reason);

        var (auth, received) = await serverTask;
        listener.Stop();

        Assert.NotNull(auth);
        Assert.Equal(MessageType.Auth, auth!.Type);
        Assert.Equal("alice", auth.Username);
        Assert.Equal("pw", auth.Password);

        Assert.Collection(
            received,
            m =>
            {
                Assert.Equal(MessageType.Chat, m.Type);
                Assert.Equal("alice", m.Username);
                Assert.Equal("hello world", m.Text);
            },
            m => Assert.Equal(MessageType.UserList, m.Type),
            m =>
            {
                Assert.Equal(MessageType.Private, m.Type);
                Assert.Equal("alice", m.Username);
                Assert.Equal("bob", m.To);
                Assert.Equal("hey there", m.Text);
            });
    }

    [Fact]
    public async Task RunAsync_ReturnsAuthFailed_WhenServerRejectsCredentials()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var srv = await listener.AcceptTcpClientAsync();
            using var channel = new MessageChannel(srv.GetStream());
            await channel.ReadAsync();
            await channel.SendAsync(Message.AuthResult(false, "Invalid password."));
        });

        var outgoing = Channel.CreateUnbounded<string>();
        var session = new ChatSession("127.0.0.1", port, "alice", "wrong");
        using var cts = new CancellationTokenSource(Timeout);

        var reason = await session.RunAsync(outgoing.Reader, cts.Token);

        await serverTask;
        listener.Stop();

        Assert.Equal(ChatSession.SessionEndReason.AuthFailed, reason);
    }

    [Fact]
    public async Task RunAsync_RendersInboundMessage_AndReportsDisconnectOnServerClose()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var srv = await listener.AcceptTcpClientAsync();
            using var channel = new MessageChannel(srv.GetStream());
            await channel.ReadAsync();
            await channel.SendAsync(Message.AuthResult(true, "welcome"));
            await channel.SendAsync(Message.Chat("server", "hello from server"));
            await Task.Delay(200);
            // Channel disposes here, closing the socket so the client observes a drop.
        });

        var outgoing = Channel.CreateUnbounded<string>();
        var session = new ChatSession("127.0.0.1", port, "alice", "pw");
        using var cts = new CancellationTokenSource(Timeout);

        var capture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(capture);

        ChatSession.SessionEndReason reason;
        try
        {
            reason = await session.RunAsync(outgoing.Reader, cts.Token);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        await serverTask;
        listener.Stop();

        Assert.Equal(ChatSession.SessionEndReason.Disconnected, reason);
        Assert.Contains("server: hello from server", capture.ToString());
    }
}
