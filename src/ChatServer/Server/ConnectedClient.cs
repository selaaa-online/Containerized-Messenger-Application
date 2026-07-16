using Shared;

namespace ChatServer.Server;

/// <summary>
/// Represents a single authenticated client connection: its identity plus the
/// channel used to deliver messages to it.
/// </summary>
public sealed class ConnectedClient
{
    public ConnectedClient(int userId, string username, MessageChannel channel)
    {
        UserId = userId;
        Username = username;
        Channel = channel;
    }

    public int UserId { get; }

    public string Username { get; }

    public MessageChannel Channel { get; }
}
