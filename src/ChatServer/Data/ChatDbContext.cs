using Microsoft.EntityFrameworkCore;

namespace ChatServer.Data;

/// <summary>
/// EF Core (code-first) database context for the chat server. Backed by PostgreSQL
/// via the Npgsql provider.
/// </summary>
public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<ChatLog> ChatLogs => Set<ChatLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<ChatLog>(entity =>
        {
            entity.HasOne(c => c.Sender)
                  .WithMany(u => u.Messages)
                  .HasForeignKey(c => c.SenderId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => c.Timestamp);
        });

        base.OnModelCreating(modelBuilder);
    }
}
