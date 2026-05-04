using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Trader.Infrastructure.Persistence;

/// <summary>
/// Resolves the MySQL connection string from either <c>ConnectionStrings:MySQL</c> or discrete
/// <c>Database:Host</c>, <c>Database:Name</c>, <c>Database:UserId</c>, <c>Database:Password</c> (and optional port / SSL).
/// </summary>
public static class MySqlConnectionStringResolver
{
    public static string? Resolve(IConfiguration configuration)
    {
        var fromConnectionString =
            configuration.GetConnectionString("MySQL")
            ?? configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromConnectionString))
            return fromConnectionString;

        var db = configuration.GetSection("Database");
        var host = db["Host"];
        var name = db["Name"];
        var userId = db["UserId"] ?? db["User"];
        var password = db["Password"];

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(password))
            return null;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Database = name,
            UserID = userId,
            Password = password,
        };

        var portValue = db["Port"];
        if (!string.IsNullOrWhiteSpace(portValue) && uint.TryParse(portValue, out var port))
            builder.Port = port;

        var sslMode = db["SslMode"];
        if (!string.IsNullOrWhiteSpace(sslMode)
            && Enum.TryParse<MySqlSslMode>(sslMode, ignoreCase: true, out var ssl))
            builder.SslMode = ssl;

        return builder.ConnectionString;
    }
}
