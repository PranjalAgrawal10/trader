using MySqlConnector;

namespace Trader.Infrastructure.Persistence;

/// <summary>
/// Ensures the catalog named in the connection string exists (MySQL does not create it automatically for EF migrations).
/// </summary>
public static class MySqlBootstrap
{
    public static async Task EnsureDatabaseExistsAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var builder = new MySqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
            return;

        builder.Database = string.Empty;

        await using var conn = new MySqlConnection(builder.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        var safeDb = database.Replace("`", "``", StringComparison.Ordinal);
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{safeDb}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
