using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

/// <summary>
/// A single unit of communication in the chat protocol. Messages are serialized
/// to JSON and framed by a trailing newline character ('\n') when written to the
/// network stream, which keeps the wire protocol simple and human-readable while
/// still supporting structured data.
/// </summary>
public class Message
{
    /// <summary>The category of this message.</summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// For <see cref="MessageType.Auth"/>: the username being authenticated.
    /// For chat/private/system messages: the sender's username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>Only used for <see cref="MessageType.Auth"/> requests.</summary>
    public string? Password { get; set; }

    /// <summary>Recipient username for <see cref="MessageType.Private"/> messages.</summary>
    public string? To { get; set; }

    /// <summary>The human-readable message body.</summary>
    public string? Text { get; set; }

    /// <summary>Result flag for <see cref="MessageType.AuthResult"/> messages.</summary>
    public bool Success { get; set; }

    /// <summary>UTC time the message was created.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializes this message to a compact single-line JSON string.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Parses a JSON string produced by <see cref="ToJson"/> back into a message.</summary>
    public static Message? FromJson(string json) =>
        JsonSerializer.Deserialize<Message>(json, SerializerOptions);

    // ---- Convenience factory helpers ----

    public static Message Auth(string username, string password) =>
        new() { Type = MessageType.Auth, Username = username, Password = password };

    public static Message AuthResult(bool success, string text) =>
        new() { Type = MessageType.AuthResult, Success = success, Text = text };

    public static Message Chat(string username, string text) =>
        new() { Type = MessageType.Chat, Username = username, Text = text };

    public static Message PrivateMessage(string from, string to, string text) =>
        new() { Type = MessageType.Private, Username = from, To = to, Text = text };

    public static Message UserListRequest() =>
        new() { Type = MessageType.UserList };

    public static Message UserListResult(string text) =>
        new() { Type = MessageType.UserList, Text = text };

    public static Message System(string text) =>
        new() { Type = MessageType.System, Text = text };
}
