using System.ComponentModel.DataAnnotations;

namespace ChatServer.Data;

/// <summary>
/// A persisted record of a single chat message. Broadcast messages have a
/// <c>null</c> <see cref="Recipient"/>; private messages record the target user.
/// </summary>
public class ChatLog
{
    public int Id { get; set; }

    public int SenderId { get; set; }

    public User? Sender { get; set; }

    [MaxLength(64)]
    public string SenderUsername { get; set; } = string.Empty;

    /// <summary>Recipient username for private messages; <c>null</c> for broadcasts.</summary>
    [MaxLength(64)]
    public string? Recipient { get; set; }

    public bool IsPrivate { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
