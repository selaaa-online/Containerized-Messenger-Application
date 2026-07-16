using Microsoft.EntityFrameworkCore;

namespace ChatServer.Data;

/// <summary>
/// A minimal thread-safe <see cref="IDbContextFactory{TContext}"/> that produces a
/// fresh short-lived <see cref="ChatDbContext"/> per operation from shared options.
/// </summary>
public class ChatDbContextRuntimeFactory : IDbContextFactory<ChatDbContext>
{
    private readonly DbContextOptions<ChatDbContext> _options;

    public ChatDbContextRuntimeFactory(DbContextOptions<ChatDbContext> options)
    {
        _options = options;
    }

    public ChatDbContext CreateDbContext() => new(_options);
}
