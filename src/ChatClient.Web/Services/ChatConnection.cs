using System.Net.Sockets;
using Shared;

namespace ChatClient.Web.Services;

/// <summary>
/// Per-circuit chat connection for the web UI. Each browser tab gets its own scoped
/// instance, which owns a single TCP connection to the chat server via the shared
/// <see cref="MessageChannel"/>. Inbound messages are read on a background loop and
/// surfaced to the UI through the <see cref="Changed"/> event.
/// </summary>
public sealed class ChatConnection : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<ChatEntry> _messages = new();
    private readonly List<string> _users = new();

    private TcpClient? _tcp;
    private MessageChannel? _channel;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    /// <summary>Raised whenever the message list or roster changes. UI subscribes to refresh.</summary>
    public event Action? Changed;

    public string? Username { get; private set; }

    public bool IsConnected { get; private set; }

    public IReadOnlyList<ChatEntry> Messages
    {
        get { lock (_gate) { return _messages.ToArray(); } }
    }

    public IReadOnlyList<string> OnlineUsers
    {
        get { lock (_gate) { return _users.ToArray(); } }
    }

    /// <summary>Connects and authenticates. Returns <c>null</c> on success or an error message.</summary>
    public async Task<string?> ConnectAsync(string host, int port, string username, string password)
    {
        if (IsConnected)
        {
            return "Already connected.";
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return "Username and password are required.";
        }

        try
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port);
            var channel = new MessageChannel(tcp.GetStream());

            await channel.SendAsync(Message.Auth(username, password));
            var response = await channel.ReadAsync();

            if (response is null)
            {
                tcp.Close();
                return "Server closed the connection during authentication.";
            }

            if (response.Type != MessageType.AuthResult || !response.Success)
            {
                channel.Dispose();
                return response.Text ?? "Authentication failed.";
            }

            _tcp = tcp;
            _channel = channel;
            Username = username;
            IsConnected = true;
            _cts = new CancellationTokenSource();

            AddEntry(ChatEntryKind.Info, response.Text ?? $"Connected as {username}.");
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            await RequestUserListAsync();
            return null;
        }
        catch (Exception ex)
        {
            return $"Unable to connect: {ex.Message}";
        }
    }

    public Task SendChatAsync(string text) =>
        SendAsync(Message.Chat(Username ?? string.Empty, text));

    public Task SendPrivateAsync(string to, string text) =>
        SendAsync(Message.PrivateMessage(Username ?? string.Empty, to, text));

    public Task RequestUserListAsync() =>
        SendAsync(Message.UserListRequest());

    public async Task DisconnectAsync()
    {
        await CleanupAsync();
        IsConnected = false;
        AddEntry(ChatEntryKind.System, "Disconnected.");
        Changed?.Invoke();
    }

    private async Task SendAsync(Message message)
    {
        var channel = _channel;
        if (channel is null || !IsConnected)
        {
            return;
        }

        try
        {
            await channel.SendAsync(message);
        }
        catch
        {
            await HandleDropAsync();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await _channel!.ReadAsync(ct);
                if (message is null)
                {
                    break; // server closed the connection
                }

                Handle(message);
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch
        {
            // Treated as a dropped connection below.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await HandleDropAsync();
            }
        }
    }

    private void Handle(Message message)
    {
        switch (message.Type)
        {
            case MessageType.Chat:
                AddEntry(ChatEntryKind.Chat, $"{message.Username}: {message.Text}");
                break;

            case MessageType.Private:
                AddEntry(ChatEntryKind.Private, $"{message.Username} → {message.To}: {message.Text}");
                break;

            case MessageType.UserList:
                UpdateUsers(message.Text);
                break;

            case MessageType.System:
                AddEntry(ChatEntryKind.System, message.Text ?? string.Empty);
                // Join/leave notices mean the roster changed — refresh it.
                _ = RequestUserListAsync();
                break;

            case MessageType.AuthResult:
                AddEntry(ChatEntryKind.Info, message.Text ?? string.Empty);
                break;
        }
    }

    private void UpdateUsers(string? text)
    {
        lock (_gate)
        {
            _users.Clear();
            if (!string.IsNullOrWhiteSpace(text) && text != "(none)")
            {
                _users.AddRange(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
    }

    private async Task HandleDropAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        IsConnected = false;
        AddEntry(ChatEntryKind.System, "Connection lost.");
        Changed?.Invoke();
        await Task.CompletedTask;
    }

    private void AddEntry(ChatEntryKind kind, string text)
    {
        lock (_gate)
        {
            _messages.Add(new ChatEntry(kind, text, DateTimeOffset.Now));
            if (_messages.Count > 500)
            {
                _messages.RemoveAt(0);
            }
        }
    }

    private async Task CleanupAsync()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }

        _channel?.Dispose();
        _tcp?.Close();

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop; } catch { /* ignore */ }
        }

        _cts?.Dispose();
        _channel = null;
        _tcp = null;
        _cts = null;
        _receiveLoop = null;
    }

    public async ValueTask DisposeAsync() => await CleanupAsync();
}

public enum ChatEntryKind
{
    Chat,
    Private,
    System,
    Info
}

public sealed record ChatEntry(ChatEntryKind Kind, string Text, DateTimeOffset Timestamp);
