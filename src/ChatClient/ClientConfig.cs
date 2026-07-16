namespace ChatClient;

/// <summary>
/// Client configuration sourced from environment variables (with defaults) and
/// optionally overridden by interactive prompts for credentials.
/// </summary>
public static class ClientConfig
{
    public static string ServerHost =>
        Environment.GetEnvironmentVariable("SERVER_HOST") ?? "localhost";

    public static int ServerPort =>
        int.TryParse(Environment.GetEnvironmentVariable("SERVER_PORT"), out var port) ? port : 9000;

    public static string? Username => Environment.GetEnvironmentVariable("CHAT_USERNAME");

    public static string? Password => Environment.GetEnvironmentVariable("CHAT_PASSWORD");
}
