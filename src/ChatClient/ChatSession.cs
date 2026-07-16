using System.Net.Sockets;
using System.Threading.Channels;
using Shared;

namespace ChatClient;

/// <summary>
/// Manages the lifetime of a single TCP connection to the chat server: performing
/// the authentication handshake, printing inbound messages, and forwarding the
/// user's outbound messages until the connection ends.
/// </summary>
public class ChatSession
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;

    public ChatSession(string host, int port, string username, string password)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
    }

    public enum SessionEndReason
    {
        Disconnected,   // transient — caller should try to reconnect
        AuthFailed,     // permanent — bad credentials or name in use
        UserQuit        // user asked to exit
    }

    /// <summary>
    /// Opens the connection and runs until it ends. Outbound user input is consumed
    /// from <paramref name="outgoing"/>.
    /// </summary>
    public async Task<SessionEndReason> RunAsync(ChannelReader<string> outgoing, CancellationToken ct)
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(_host, _port, ct);
        using var channel = new MessageChannel(tcpClient.GetStream());

        // ---- Authenticate ----
        await channel.SendAsync(Message.Auth(_username, _password), ct);
        var authResponse = await channel.ReadAsync(ct);
        if (authResponse is null)
        {
            return SessionEndReason.Disconnected;
        }

        if (authResponse.Type != MessageType.AuthResult || !authResponse.Success)
        {
            Console.WriteLine($"[auth] {authResponse.Text}");
            return SessionEndReason.AuthFailed;
        }

        Console.WriteLine($"[auth] {authResponse.Text}");
        Console.WriteLine("Connected. Type a message and press Enter. Commands: /list, /msg <user> <text>, /quit");

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receiveTask = ReceiveLoopAsync(channel, sessionCts.Token);
        var sendTask = SendLoopAsync(channel, outgoing, sessionCts.Token);

        var completed = await Task.WhenAny(receiveTask, sendTask);
        sessionCts.Cancel();

        try
        {
            await Task.WhenAll(receiveTask, sendTask);
        }
        catch
        {
            // Ignore cancellation/IO errors during teardown.
        }

        if (completed == sendTask && sendTask.Result == SessionEndReason.UserQuit)
        {
            return SessionEndReason.UserQuit;
        }

        return SessionEndReason.Disconnected;
    }

    /// <summary>Reads inbound messages from the server and renders them to the console.</summary>
    private static async Task ReceiveLoopAsync(MessageChannel channel, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Message? message;
            try
            {
                message = await channel.ReadAsync(ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return; // connection dropped
            }

            if (message is null)
            {
                return; // server closed the connection
            }

            Console.WriteLine(Render(message));
        }
    }

    /// <summary>Forwards buffered user input to the server, interpreting slash commands.</summary>
    private async Task<SessionEndReason> SendLoopAsync(MessageChannel channel, ChannelReader<string> outgoing, CancellationToken ct)
    {
        try
        {
            await foreach (var line in outgoing.ReadAllAsync(ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase))
                {
                    return SessionEndReason.UserQuit;
                }

                if (line.Equals("/list", StringComparison.OrdinalIgnoreCase))
                {
                    await channel.SendAsync(Message.UserListRequest(), ct);
                    continue;
                }

                if (line.StartsWith("/msg ", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = line[5..].TrimStart();
                    var split = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (split.Length < 2)
                    {
                        Console.WriteLine("[client] Usage: /msg <user> <message>");
                        continue;
                    }

                    await channel.SendAsync(Message.PrivateMessage(_username, split[0], split[1]), ct);
                    continue;
                }

                await channel.SendAsync(Message.Chat(_username, line), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Session cancelled (e.g. receive loop detected a drop).
        }

        return SessionEndReason.Disconnected;
    }

    private static string Render(Message message) => message.Type switch
    {
        MessageType.System => $"[system] {message.Text}",
        MessageType.Private => $"[private] {message.Username} -> {message.To}: {message.Text}",
        MessageType.Chat => $"{message.Username}: {message.Text}",
        MessageType.UserList => $"[users online] {message.Text}",
        MessageType.AuthResult => $"[auth] {message.Text}",
        _ => $"{message.Text}"
    };
}
