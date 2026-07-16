namespace Shared;

/// <summary>
/// The kinds of messages exchanged between the chat client and server.
/// </summary>
public enum MessageType
{
    /// <summary>Client -> Server: authentication request (login or auto-register).</summary>
    Auth,

    /// <summary>Server -> Client: result of an authentication attempt.</summary>
    AuthResult,

    /// <summary>Broadcast chat message visible to every connected client.</summary>
    Chat,

    /// <summary>Directed message delivered only to a single recipient.</summary>
    Private,

    /// <summary>
    /// Request (client -> server, no body) or response (server -> client, with the
    /// list of online usernames in <see cref="Message.Text"/>) for who is online.
    /// </summary>
    UserList,

    /// <summary>Server -> Client: informational/system notification.</summary>
    System
}
