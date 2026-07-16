using ChatServer.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServer.Tests;

/// <summary>
/// Builds <see cref="ChatDbContext"/> factories backed by the EF Core in-memory
/// provider. Each call uses a unique database name so tests stay isolated, while a
/// single factory instance shares one in-memory store across the contexts it creates.
/// </summary>
internal static class InMemoryDb
{
    public static ChatDbContextRuntimeFactory NewFactory()
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase($"chat-tests-{Guid.NewGuid():N}")
            .Options;
        return new ChatDbContextRuntimeFactory(options);
    }
}
