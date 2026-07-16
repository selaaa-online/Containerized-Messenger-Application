using ChatServer.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServer.Auth;

/// <summary>
/// Handles user authentication. If the supplied username does not yet exist it is
/// registered automatically with the provided password; otherwise the password is
/// verified against the stored hash.
/// </summary>
public class AuthService
{
    private readonly IDbContextFactory<ChatDbContext> _dbFactory;

    public AuthService(IDbContextFactory<ChatDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public record AuthOutcome(bool Success, string Message, User? User);

    public async Task<AuthOutcome> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthOutcome(false, "Username and password are required.", null);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
        {
            var (hash, salt) = PasswordHasher.Hash(password);
            user = new User { Username = username, PasswordHash = hash, PasswordSalt = salt };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return new AuthOutcome(true, $"Registered and logged in as '{username}'.", user);
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            return new AuthOutcome(false, "Invalid password.", null);
        }

        return new AuthOutcome(true, $"Logged in as '{username}'.", user);
    }
}
