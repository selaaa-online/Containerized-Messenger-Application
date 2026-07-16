using System.ComponentModel.DataAnnotations;

namespace ChatServer.Data;

/// <summary>
/// A registered chat user. Passwords are never stored in plain text; only a
/// PBKDF2 hash and its per-user salt are persisted.
/// </summary>
public class User
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(256)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(256)]
    public string PasswordSalt { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChatLog> Messages { get; set; } = new List<ChatLog>();
}
