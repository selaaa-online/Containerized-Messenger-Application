using System.Net;
using System.Net.Sockets;
using ChatServer.Auth;
using ChatServer.Logging;
using ChatServer.Server;
using Shared;
using Xunit;

namespace ChatServer.Tests;

/// <summary>
/// End-to-end tests that start a real <see cref="ChatHost"/> on a loopback port and
/// connect genuine TCP clients, exercising authentication, broadcast, private
/// messaging and the online-user listing across the full network path.
/// </summary>
public class ChatHostIntegrationTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Broadcast_ReachesAllConnectedClients()
    {
        await using var server = await ChatServerHarness.StartAsync();
        using var alice = await server.ConnectAsync("alice", "pw");
        using var bob = await server.ConnectAsync("bob", "pw");

        await alice.SendAsync(Message.Chat("alice", "hello everyone"));

        var atAlice = await ReadUntil(alice, m => m.Type == MessageType.Chat && m.Text == "hello everyone");
        var atBob = await ReadUntil(bob, m => m.Type == MessageType.Chat && m.Text == "hello everyone");

        Assert.Equal("alice", atAlice.Username);
        Assert.Equal("alice", atBob.Username);
    }

    [Fact]
    public async Task PrivateMessage_DeliveredToRecipientAndEchoedToSender()
    {
        await using var server = await ChatServerHarness.StartAsync();
        using var alice = await server.ConnectAsync("alice", "pw");
        using var bob = await server.ConnectAsync("bob", "pw");

        await alice.SendAsync(Message.PrivateMessage("alice", "bob", "secret"));

        var atBob = await ReadUntil(bob, m => m.Type == MessageType.Private);
        var atAlice = await ReadUntil(alice, m => m.Type == MessageType.Private);

        Assert.Equal("secret", atBob.Text);
        Assert.Equal("bob", atBob.To);
        Assert.Equal("alice", atBob.Username);
        Assert.Equal("secret", atAlice.Text); // echoed back to sender
    }

    [Fact]
    public async Task PrivateMessage_ToOfflineUser_ReturnsSystemNotice()
    {
        await using var server = await ChatServerHarness.StartAsync();
        using var alice = await server.ConnectAsync("alice", "pw");

        await alice.SendAsync(Message.PrivateMessage("alice", "ghost", "hi"));

        var notice = await ReadUntil(alice, m => m.Type == MessageType.System);
        Assert.Contains("ghost", notice.Text);
        Assert.Contains("not online", notice.Text);
    }

    [Fact]
    public async Task UserList_ReturnsAllConnectedUsers()
    {
        await using var server = await ChatServerHarness.StartAsync();
        using var alice = await server.ConnectAsync("alice", "pw");
        using var bob = await server.ConnectAsync("bob", "pw");

        await alice.SendAsync(Message.UserListRequest());

        var list = await ReadUntil(alice, m => m.Type == MessageType.UserList && m.Text is not null);
        Assert.Contains("alice", list.Text);
        Assert.Contains("bob", list.Text);
    }

    [Fact]
    public async Task Authentication_Fails_WithWrongPassword()
    {
        await using var server = await ChatServerHarness.StartAsync();

        // Register alice with the correct password, then disconnect.
        using (await server.ConnectAsync("alice", "correct")) { }

        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.Port);
        using var channel = new MessageChannel(tcp.GetStream());
        await channel.SendAsync(Message.Auth("alice", "wrong"));

        var response = await ReadWithTimeout(channel);
        Assert.NotNull(response);
        Assert.Equal(MessageType.AuthResult, response!.Type);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task Client_Joining_NotifiesExistingClients()
    {
        await using var server = await ChatServerHarness.StartAsync();
        using var alice = await server.ConnectAsync("alice", "pw");

        using var bob = await server.ConnectAsync("bob", "pw");

        var joined = await ReadUntil(alice, m => m.Type == MessageType.System && m.Text!.Contains("bob"));
        Assert.Contains("joined", joined.Text);
    }

    private static async Task<Message> ReadUntil(MessageChannel channel, Func<Message, bool> predicate)
    {
        var deadline = DateTime.UtcNow + ReadTimeout;
        while (DateTime.UtcNow < deadline)
        {
            using var cts = new CancellationTokenSource(deadline - DateTime.UtcNow);
            Message? message;
            try
            {
                message = await channel.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (message is null)
            {
                break;
            }

            if (predicate(message))
            {
                return message;
            }
        }

        throw new TimeoutException("Expected message was not received in time.");
    }

    private static async Task<Message?> ReadWithTimeout(MessageChannel channel)
    {
        using var cts = new CancellationTokenSource(ReadTimeout);
        return await channel.ReadAsync(cts.Token);
    }
}

/// <summary>Starts a <see cref="ChatHost"/> for a single test and cleans it up on dispose.</summary>
internal sealed class ChatServerHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Task _runTask;
    private readonly string _logFile;

    public int Port { get; }

    private ChatServerHarness(int port, CancellationTokenSource cts, Task runTask, string logFile)
    {
        Port = port;
        _cts = cts;
        _runTask = runTask;
        _logFile = logFile;
    }

    public static async Task<ChatServerHarness> StartAsync()
    {
        var factory = InMemoryDb.NewFactory();
        var logFile = Path.Combine(Path.GetTempPath(), $"chathost-{Guid.NewGuid():N}.log");
        var logger = new MessageLogger(factory, logFile);
        var authService = new AuthService(factory);

        var port = GetFreePort();
        var host = new ChatHost(port, authService, logger);
        var cts = new CancellationTokenSource();
        var runTask = host.RunAsync(cts.Token);

        // Give the listener a brief moment to bind before clients connect.
        await Task.Delay(100);
        return new ChatServerHarness(port, cts, runTask, logFile);
    }

    /// <summary>Connects a client and completes the authentication handshake.</summary>
    public async Task<MessageChannel> ConnectAsync(string username, string password)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        var channel = new MessageChannel(tcp.GetStream());

        await channel.SendAsync(Message.Auth(username, password));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await channel.ReadAsync(cts.Token);
        if (result is null || result.Type != MessageType.AuthResult || !result.Success)
        {
            channel.Dispose();
            throw new InvalidOperationException($"Authentication failed for '{username}': {result?.Text}");
        }

        return channel;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _runTask;
        }
        catch
        {
            // Expected cancellation during shutdown.
        }
        finally
        {
            _cts.Dispose();
            if (File.Exists(_logFile))
            {
                File.Delete(_logFile);
            }
        }
    }
}
