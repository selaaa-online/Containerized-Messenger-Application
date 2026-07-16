using ChatServer.Auth;
using ChatServer.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChatServer.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_RegistersNewUser_OnFirstLogin()
    {
        var factory = InMemoryDb.NewFactory();
        var service = new AuthService(factory);

        var outcome = await service.AuthenticateAsync("alice", "pw");

        Assert.True(outcome.Success);
        Assert.NotNull(outcome.User);
        Assert.Equal("alice", outcome.User!.Username);

        await using var db = factory.CreateDbContext();
        Assert.True(await db.Users.AnyAsync(u => u.Username == "alice"));
    }

    [Fact]
    public async Task AuthenticateAsync_LogsInExistingUser_WithCorrectPassword()
    {
        var factory = InMemoryDb.NewFactory();
        var service = new AuthService(factory);
        await service.AuthenticateAsync("alice", "pw"); // register

        var outcome = await service.AuthenticateAsync("alice", "pw");

        Assert.True(outcome.Success);
        Assert.NotNull(outcome.User);
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WithWrongPassword()
    {
        var factory = InMemoryDb.NewFactory();
        var service = new AuthService(factory);
        await service.AuthenticateAsync("alice", "pw"); // register

        var outcome = await service.AuthenticateAsync("alice", "wrong");

        Assert.False(outcome.Success);
        Assert.Null(outcome.User);
        Assert.Equal("Invalid password.", outcome.Message);
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("alice", "")]
    [InlineData("  ", "pw")]
    [InlineData("alice", "  ")]
    public async Task AuthenticateAsync_Fails_WhenCredentialsMissing(string username, string password)
    {
        var factory = InMemoryDb.NewFactory();
        var service = new AuthService(factory);

        var outcome = await service.AuthenticateAsync(username, password);

        Assert.False(outcome.Success);
        Assert.Null(outcome.User);
    }

    [Fact]
    public async Task AuthenticateAsync_DoesNotDuplicateUser_OnRepeatedLogins()
    {
        var factory = InMemoryDb.NewFactory();
        var service = new AuthService(factory);

        await service.AuthenticateAsync("alice", "pw");
        await service.AuthenticateAsync("alice", "pw");

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Username == "alice"));
    }
}
