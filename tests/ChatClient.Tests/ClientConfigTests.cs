using ChatClient;
using Xunit;

namespace ChatClient.Tests;

public class ClientConfigTests
{
    [Fact]
    public void ServerHost_DefaultsToLocalhost_WhenUnset()
    {
        WithEnv("SERVER_HOST", null, () => Assert.Equal("localhost", ClientConfig.ServerHost));
    }

    [Fact]
    public void ServerHost_ReadsEnvironmentVariable()
    {
        WithEnv("SERVER_HOST", "chat-server", () => Assert.Equal("chat-server", ClientConfig.ServerHost));
    }

    [Fact]
    public void ServerPort_DefaultsTo9000_WhenUnset()
    {
        WithEnv("SERVER_PORT", null, () => Assert.Equal(9000, ClientConfig.ServerPort));
    }

    [Fact]
    public void ServerPort_ReadsEnvironmentVariable()
    {
        WithEnv("SERVER_PORT", "4242", () => Assert.Equal(4242, ClientConfig.ServerPort));
    }

    [Fact]
    public void ServerPort_FallsBackToDefault_WhenValueIsNotNumeric()
    {
        WithEnv("SERVER_PORT", "abc", () => Assert.Equal(9000, ClientConfig.ServerPort));
    }

    [Fact]
    public void Username_And_Password_AreNull_WhenUnset()
    {
        WithEnv("CHAT_USERNAME", null, () =>
            WithEnv("CHAT_PASSWORD", null, () =>
            {
                Assert.Null(ClientConfig.Username);
                Assert.Null(ClientConfig.Password);
            }));
    }

    [Fact]
    public void Username_And_Password_ReadEnvironmentVariables()
    {
        WithEnv("CHAT_USERNAME", "alice", () =>
            WithEnv("CHAT_PASSWORD", "secret", () =>
            {
                Assert.Equal("alice", ClientConfig.Username);
                Assert.Equal("secret", ClientConfig.Password);
            }));
    }

    private static void WithEnv(string name, string? value, Action assert)
    {
        var original = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            assert();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
