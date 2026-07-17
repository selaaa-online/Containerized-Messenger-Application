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

    // Optional TLS mode for managed providers (e.g. Azure requires SSL). When unset,
    // the connection string omits SSL settings and Npgsql uses its defaults.
    private static string? DbSslMode => Environment.GetEnvironmentVariable("POSTGRES_SSL_MODE");

    /// <summary>Builds the Npgsql connection string from the configured settings.</summary>
    public static string ConnectionString
    {
        get
        {
            var connectionString =
                $"Host={DbHost};Port={DbPort};Database={DbName};Username={DbUser};Password={DbPassword}";

            if (!string.IsNullOrWhiteSpace(DbSslMode))
            {
                connectionString += $";SSL Mode={DbSslMode};Trust Server Certificate=true";
            }

            return connectionString;
        }
    }
}
