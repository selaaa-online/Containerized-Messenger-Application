using ChatServer;
using Xunit;

namespace ChatServer.Tests;

public class ServerConfigTests
{
    [Fact]
    public void Port_DefaultsTo9000_WhenUnset()
    {
        WithEnv("SERVER_PORT", null, () => Assert.Equal(9000, ServerConfig.Port));
    }

    [Fact]
    public void Port_ReadsEnvironmentVariable()
    {
        WithEnv("SERVER_PORT", "12345", () => Assert.Equal(12345, ServerConfig.Port));
    }

    [Fact]
    public void Port_FallsBackToDefault_WhenValueIsNotNumeric()
    {
        WithEnv("SERVER_PORT", "not-a-number", () => Assert.Equal(9000, ServerConfig.Port));
    }

    [Fact]
    public void LogFilePath_DefaultsToLogsChatLog_WhenUnset()
    {
        WithEnv("LOG_FILE_PATH", null, () => Assert.Equal("logs/chat.log", ServerConfig.LogFilePath));
    }

    [Fact]
    public void ConnectionString_UsesDefaults_WhenUnset()
    {
        WithEnv("POSTGRES_HOST", null, () =>
            WithEnv("POSTGRES_DB", null, () =>
            {
                var cs = ServerConfig.ConnectionString;
                Assert.Contains("Host=localhost", cs);
                Assert.Contains("Database=chatdb", cs);
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
