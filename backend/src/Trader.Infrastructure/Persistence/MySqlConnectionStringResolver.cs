using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Trader.Infrastructure.Persistence;

/// <summary>
/// Builds a MySQL ADO.NET connection string only from discrete configuration:
/// <c>Database:Host</c>, <c>Name</c>, <c>Username</c>/<c>UserId</c>, <c>Password</c>, optional <c>Port</c>, <c>SslMode</c>,
/// plus common <c>MYSQL_*</c> / <c>DB_*</c> / <c>DATABASE_*</c> env aliases. See <c>backend/src/Trader.Api/.env.example</c>.
/// </summary>
public static class MySqlConnectionStringResolver
{
    public static string? Resolve(IConfiguration configuration) =>
        TryBuildFromDiscreteDatabaseSection(configuration);

    /// <summary>
    /// Human-readable list of required discrete fields still missing (after <c>Database:*</c> and common <c>MYSQL_*</c> / <c>DB_*</c> aliases).
    /// </summary>
    public static string DescribeDiscreteGaps(IConfiguration configuration)
    {
        var p = LoadDiscreteParts(configuration);
        var gaps = new List<string>(4);
        if (p.Host == null)
            gaps.Add("Host (**Database__Host**, **MYSQL_HOST**, **DATABASE_HOST**, **DB_HOST**)");
        if (p.Name == null)
            gaps.Add("Name (**Database__Name**, **MYSQL_DATABASE**, **DATABASE_NAME**, **DB_NAME**)");
        if (p.User == null)
            gaps.Add("Username (**Database__Username**, **Database__UserId**, **MYSQL_USER**, **DB_USER**)");
        if (p.Password == null)
            gaps.Add("Password (**Database__Password**, **MYSQL_PASSWORD**, **DB_PASSWORD**)");

        return gaps.Count == 0 ? string.Empty : "Incomplete discrete DB config — still missing: " + string.Join("; ", gaps) + ". ";
    }

    private readonly struct DiscreteParts
    {
        public string? Host { get; init; }
        public string? Name { get; init; }
        public string? User { get; init; }
        public string? Password { get; init; }
        public string? Port { get; init; }
        public string? SslMode { get; init; }
    }

    private static DiscreteParts LoadDiscreteParts(IConfiguration configuration)
    {
        var db = configuration.GetSection("Database");
        return new DiscreteParts
        {
            Host = FirstNonEmpty(
                db["Host"],
                configuration["MYSQL_HOST"],
                configuration["DATABASE_HOST"],
                configuration["DB_HOST"]),
            Name = FirstNonEmpty(
                db["Name"],
                configuration["MYSQL_DATABASE"],
                configuration["DATABASE_NAME"],
                configuration["DB_NAME"],
                configuration["DB_DATABASE"]),
            User = FirstNonEmpty(
                db["Username"],
                db["UserId"],
                db["User"],
                configuration["MYSQL_USER"],
                configuration["DATABASE_USER"],
                configuration["DB_USER"]),
            Password = FirstNonEmpty(
                db["Password"],
                configuration["MYSQL_PASSWORD"],
                configuration["DATABASE_PASSWORD"],
                configuration["DB_PASSWORD"],
                configuration["MYSQL_PWD"]),
            Port = FirstNonEmpty(
                db["Port"],
                configuration["MYSQL_PORT"],
                configuration["DATABASE_PORT"],
                configuration["DB_PORT"]),
            SslMode = FirstNonEmpty(
                db["SslMode"],
                configuration["MYSQL_SSL_MODE"],
                configuration["DATABASE_SSL_MODE"]),
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private static string? TryBuildFromDiscreteDatabaseSection(IConfiguration configuration)
    {
        var p = LoadDiscreteParts(configuration);
        if (p.Host == null || p.Name == null || p.User == null || p.Password == null)
            return null;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = p.Host,
            Database = p.Name,
            UserID = p.User,
            Password = p.Password,
        };

        if (!string.IsNullOrWhiteSpace(p.Port) && uint.TryParse(p.Port, out var port))
            builder.Port = port;

        if (!string.IsNullOrWhiteSpace(p.SslMode)
            && Enum.TryParse<MySqlSslMode>(p.SslMode, ignoreCase: true, out var ssl))
            builder.SslMode = ssl;

        return builder.ConnectionString;
    }
}
