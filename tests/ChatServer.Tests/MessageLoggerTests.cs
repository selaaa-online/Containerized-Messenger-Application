using ChatServer.Data;
using ChatServer.Logging;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatServer.Tests;

public class MessageLoggerTests : IDisposable
{
    private readonly string _logFile = Path.Combine(Path.GetTempPath(), $"chatlog-{Guid.NewGuid():N}.log");

    [Fact]
    public async Task LogMessageAsync_Broadcast_WritesFileAndDatabaseRow()
    {
        var factory = InMemoryDb.NewFactory();
        var logger = new MessageLogger(factory, _logFile);

        await logger.LogMessageAsync(1, "alice", "hello all", recipient: null, isPrivate: false);

        var fileText = await File.ReadAllTextAsync(_logFile);
        Assert.Contains("[broadcast] alice: hello all", fileText);

        await using var db = factory.CreateDbContext();
        var row = await db.ChatLogs.SingleAsync();
        Assert.Equal("alice", row.SenderUsername);
        Assert.Equal("hello all", row.Content);
        Assert.False(row.IsPrivate);
        Assert.Null(row.Recipient);
    }

    [Fact]
    public async Task LogMessageAsync_Private_RecordsRecipient()
    {
        var factory = InMemoryDb.NewFactory();
        var logger = new MessageLogger(factory, _logFile);

        await logger.LogMessageAsync(1, "alice", "psst", recipient: "bob", isPrivate: true);

        var fileText = await File.ReadAllTextAsync(_logFile);
        Assert.Contains("[private to bob] alice: psst", fileText);

        await using var db = factory.CreateDbContext();
        var row = await db.ChatLogs.SingleAsync();
        Assert.True(row.IsPrivate);
        Assert.Equal("bob", row.Recipient);
    }

    [Fact]
    public async Task LogSystemAsync_WritesFile_ButNoDatabaseRow()
    {
        var factory = InMemoryDb.NewFactory();
        var logger = new MessageLogger(factory, _logFile);

        await logger.LogSystemAsync("server started");

        var fileText = await File.ReadAllTextAsync(_logFile);
        Assert.Contains("[SYSTEM] server started", fileText);

        await using var db = factory.CreateDbContext();
        Assert.Equal(0, await db.ChatLogs.CountAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_logFile))
        {
            File.Delete(_logFile);
        }
    }
}
