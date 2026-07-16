using ChatServer.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServer.Logging;

/// <summary>
/// Records chat activity to two sinks: a timestamped append-only log file and the
/// PostgreSQL database. File writes are serialized to remain safe across threads.
/// </summary>
public class MessageLogger
{
    private readonly IDbContextFactory<ChatDbContext> _dbFactory;
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public MessageLogger(IDbContextFactory<ChatDbContext> dbFactory, string logFilePath)
    {
        _dbFactory = dbFactory;
        _logFilePath = logFilePath;

        var dir = Path.GetDirectoryName(Path.GetFullPath(_logFilePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Writes a system/diagnostic line to the log file only.</summary>
    public Task LogSystemAsync(string text) =>
        AppendToFileAsync($"[SYSTEM] {text}");

    /// <summary>Persists a chat message to both the file and the database.</summary>
    public async Task LogMessageAsync(int senderId, string senderUsername, string content, string? recipient, bool isPrivate, CancellationToken ct = default)
    {
        var scope = isPrivate ? $"private to {recipient}" : "broadcast";
        await AppendToFileAsync($"[{scope}] {senderUsername}: {content}");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ChatLogs.Add(new ChatLog
        {
            SenderId = senderId,
            SenderUsername = senderUsername,
            Recipient = recipient,
            IsPrivate = isPrivate,
            Content = content,
            Timestamp = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task AppendToFileAsync(string line)
    {
        var entry = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff 'UTC'} {line}{Environment.NewLine}";
        await _fileLock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logFilePath, entry);
            Console.Write(entry);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
