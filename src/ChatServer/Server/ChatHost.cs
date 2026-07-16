using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ChatServer.Auth;
using ChatServer.Logging;
using Shared;

namespace ChatServer.Server;

/// <summary>
/// The multi-threaded chat server. Listens for TCP connections, authenticates each
/// client, then relays broadcast and private messages between all participants.
/// Each connection is serviced on its own asynchronous task.
/// </summary>
public class ChatHost
{
    private readonly int _port;
    private readonly AuthService _authService;
    private readonly MessageLogger _logger;

    // Registry of currently connected clients keyed by (case-insensitive) username.
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    public ChatHost(int port, AuthService authService, MessageLogger logger)
    {
        _port = port;
        _authService = authService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        await _logger.LogSystemAsync($"Chat server listening on port {_port}.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);
                // Fire-and-forget: each client runs independently.
                _ = HandleClientAsync(tcpClient, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            listener.Stop();
            await _logger.LogSystemAsync("Chat server stopped.");
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        var remote = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
        using var channel = new MessageChannel(tcpClient.GetStream());
        ConnectedClient? client = null;

        try
        {
            client = await AuthenticateAsync(channel, remote, cancellationToken);
            if (client is null)
            {
                return; // authentication failed or connection dropped
            }

            await _logger.LogSystemAsync($"'{client.Username}' connected from {remote}.");
            await BroadcastAsync(Message.System($"{client.Username} joined the chat."), except: client.Username);

            await ReceiveLoopAsync(client, channel, cancellationToken);
        }
        catch (IOException)
        {
            // Unexpected disconnect — treated as a normal drop below.
        }
        catch (Exception ex)
        {
            await _logger.LogSystemAsync($"Error handling {remote}: {ex.Message}");
        }
        finally
        {
            if (client is not null)
            {
                _clients.TryRemove(client.Username, out _);
                await _logger.LogSystemAsync($"'{client.Username}' disconnected.");
                await BroadcastAsync(Message.System($"{client.Username} left the chat."), except: client.Username);
            }

            tcpClient.Close();
        }
    }

    /// <summary>Runs the authentication handshake and registers the client on success.</summary>
    private async Task<ConnectedClient?> AuthenticateAsync(MessageChannel channel, string remote, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var request = await channel.ReadAsync(ct);
            if (request is null)
            {
                return null; // client disconnected during auth
            }

            if (request.Type != MessageType.Auth)
            {
                await channel.SendAsync(Message.AuthResult(false, "Authentication required."), ct);
                continue;
            }

            var outcome = await _authService.AuthenticateAsync(request.Username ?? "", request.Password ?? "", ct);
            if (!outcome.Success || outcome.User is null)
            {
                await channel.SendAsync(Message.AuthResult(false, outcome.Message), ct);
                continue;
            }

            var username = outcome.User.Username;
            var connected = new ConnectedClient(outcome.User.Id, username, channel);

            if (!_clients.TryAdd(username, connected))
            {
                await channel.SendAsync(Message.AuthResult(false, $"User '{username}' is already connected."), ct);
                continue;
            }

            await channel.SendAsync(Message.AuthResult(true, outcome.Message), ct);
            return connected;
        }

        return null;
    }

    /// <summary>Reads and dispatches messages from a single client until it disconnects.</summary>
    private async Task ReceiveLoopAsync(ConnectedClient client, MessageChannel channel, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var message = await channel.ReadAsync(ct);
            if (message is null)
            {
                break; // graceful or unexpected disconnect
            }

            switch (message.Type)
            {
                case MessageType.Private:
                    await HandlePrivateAsync(client, message, ct);
                    break;

                case MessageType.UserList:
                    await HandleUserListAsync(client, ct);
                    break;

                case MessageType.Chat:
                default:
                    await HandleBroadcastAsync(client, message, ct);
                    break;
            }
        }
    }

    private async Task HandleBroadcastAsync(ConnectedClient sender, Message message, CancellationToken ct)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await _logger.LogMessageAsync(sender.UserId, sender.Username, text, recipient: null, isPrivate: false, ct);
        await BroadcastAsync(Message.Chat(sender.Username, text), except: null);
    }

    private async Task HandlePrivateAsync(ConnectedClient sender, Message message, CancellationToken ct)
    {
        var text = message.Text?.Trim();
        var target = message.To?.Trim();

        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(text))
        {
            await sender.Channel.SendAsync(Message.System("Usage: /msg <username> <message>"), ct);
            return;
        }

        if (!_clients.TryGetValue(target, out var recipient))
        {
            await sender.Channel.SendAsync(Message.System($"User '{target}' is not online."), ct);
            return;
        }

        await _logger.LogMessageAsync(sender.UserId, sender.Username, text, recipient.Username, isPrivate: true, ct);

        var outgoing = Message.PrivateMessage(sender.Username, recipient.Username, text);
        await SafeSendAsync(recipient, outgoing);
        await SafeSendAsync(sender, outgoing); // echo to sender so they see it too
    }

    /// <summary>Replies to the requesting client with the list of currently online users.</summary>
    private async Task HandleUserListAsync(ConnectedClient requester, CancellationToken ct)
    {
        var users = _clients.Values
            .Select(c => c.Username)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var text = users.Count > 0 ? string.Join(", ", users) : "(none)";
        await requester.Channel.SendAsync(Message.UserListResult(text), ct);
    }

    /// <summary>Delivers a message to every connected client (optionally excluding one).</summary>
    private async Task BroadcastAsync(Message message, string? except)
    {
        var targets = _clients.Values
            .Where(c => except is null || !string.Equals(c.Username, except, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var target in targets)
        {
            await SafeSendAsync(target, message);
        }
    }

    /// <summary>Sends to a client, swallowing errors so one dead socket can't break the loop.</summary>
    private async Task SafeSendAsync(ConnectedClient client, Message message)
    {
        try
        {
            await client.Channel.SendAsync(message);
        }
        catch
        {
            // The receive loop for that client will detect the drop and clean it up.
            _clients.TryRemove(client.Username, out _);
        }
    }
}
