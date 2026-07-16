using ChatServer;
using ChatServer.Auth;
using ChatServer.Data;
using ChatServer.Logging;
using ChatServer.Server;
using Microsoft.EntityFrameworkCore;

// Build a pooled EF Core context factory (thread-safe, one short-lived context per op).
var options = new DbContextOptionsBuilder<ChatDbContext>()
    .UseNpgsql(ServerConfig.ConnectionString)
    .Options;
var dbFactory = new ChatDbContextRuntimeFactory(options);

// Graceful shutdown on Ctrl+C / SIGTERM.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

var logger = new MessageLogger(dbFactory, ServerConfig.LogFilePath);

// Wait for PostgreSQL to become available, then apply code-first migrations.
await ApplyMigrationsWithRetryAsync(dbFactory, logger, cts.Token);

var authService = new AuthService(dbFactory);
var host = new ChatHost(ServerConfig.Port, authService, logger);

await host.RunAsync(cts.Token);

static async Task ApplyMigrationsWithRetryAsync(
    IDbContextFactory<ChatDbContext> dbFactory,
    MessageLogger logger,
    CancellationToken ct)
{
    const int maxAttempts = 15;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct);
            await logger.LogSystemAsync("Database migrations applied.");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            await logger.LogSystemAsync($"Database not ready (attempt {attempt}/{maxAttempts}): {ex.Message}");
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    throw new InvalidOperationException("Could not connect to the database after multiple attempts.");
}
