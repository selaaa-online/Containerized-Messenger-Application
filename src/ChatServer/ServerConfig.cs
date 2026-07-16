namespace ChatServer;

/// <summary>
/// Central place for reading server configuration from environment variables,
/// with sensible defaults so the app can run locally without extra setup.
/// </summary>
public static class ServerConfig
{
    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("SERVER_PORT"), out var port) ? port : 9000;

    public static string LogFilePath =>
        Environment.GetEnvironmentVariable("LOG_FILE_PATH") ?? "logs/chat.log";

    // ---- PostgreSQL connection settings ----

    private static string DbHost => Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    private static string DbPort => Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
    private static string DbName => Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "chatdb";
    private static string DbUser => Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "chat";
    private static string DbPassword => Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "chatpassword";

    /// <summary>Builds the Npgsql connection string from the configured settings.</summary>
    public static string ConnectionString =>
        $"Host={DbHost};Port={DbPort};Database={DbName};Username={DbUser};Password={DbPassword}";
}
