using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChatServer.Data;

/// <summary>
/// Enables the EF Core CLI tools (e.g. <c>dotnet ef migrations add</c>) to create a
/// <see cref="ChatDbContext"/> at design time without running the full application.
/// </summary>
public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        optionsBuilder.UseNpgsql(ServerConfig.ConnectionString);
        return new ChatDbContext(optionsBuilder.Options);
    }
}
