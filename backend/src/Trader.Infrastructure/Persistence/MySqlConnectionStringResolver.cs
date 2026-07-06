using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Trader.Infrastructure.Persistence;

/// <summary>
/// Builds a MySQL ADO.NET connection string from, in order:
/// <list type="number">
/// <item>Full connection string / <c>mysql://</c> URL (<c>Database:ConnectionString</c>, <c>DATABASE_URL</c>, …)</item>
/// <item>Discrete <c>Database:Host</c>, <c>Name</c>, <c>Username</c>/<c>UserId</c>, <c>Password</c>, optional <c>Port</c>, <c>SslMode</c></item>
/// <item>Common <c>MYSQL_*</c> / <c>DB_*</c> / <c>DATABASE_*</c> env aliases</item>
/// </list>
/// See <c>backend/src/Trader.Api/.env.example</c>.
/// </summary>
public static class MySqlConnectionStringResolver
{
    public static string? Resolve(IConfiguration configuration) =>
        TryDirectConnectionString(configuration)
        ?? TryBuildFromDiscreteDatabaseSection(configuration);

    /// <summary>
    /// Human-readable hint when neither a direct string nor discrete fields are complete.
    /// </summary>
    public static string DescribeConfigurationGaps(IConfiguration configuration)
    {
        if (HasDirectConnectionStringCandidate(configuration))
            return "A database URL/connection string env var is present but could not be parsed. ";

        var discrete = DescribeDiscreteGaps(configuration);
        return string.IsNullOrEmpty(discrete)
            ? "No MySQL connection string or discrete Database__* settings were found. "
            : discrete;
    }

    /// <summary>
    /// Human-readable list of required discrete fields still missing (after <c>Database:*</c> and common aliases).
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
            gaps.Add("Username (**Database__Username**, **Database__UserId**, **MYSQL_USER**, **DB_USERNAME**, **DB_USER**)");
        if (p.Password == null)
            gaps.Add("Password (**Database__Password**, **MYSQL_PASSWORD**, **DB_PASSWORD**)");

        return gaps.Count == 0
            ? string.Empty
            : "Incomplete discrete DB config — still missing: " + string.Join("; ", gaps) + ". ";
    }

    private static bool HasDirectConnectionStringCandidate(IConfiguration configuration)
    {
        foreach (var raw in EnumerateDirectCandidates(configuration))
        {
            if (!string.IsNullOrWhiteSpace(raw))
                return true;
        }

        return false;
    }

    private static string? TryDirectConnectionString(IConfiguration configuration)
    {
        foreach (var raw in EnumerateDirectCandidates(configuration))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var normalized = NormalizeDirectConnectionString(raw.Trim());
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return null;
    }

    private static IEnumerable<string?> EnumerateDirectCandidates(IConfiguration configuration)
    {
        // Production / App Platform: prefer DATABASE_URL when present.
        yield return configuration["DATABASE_URL"];
        yield return configuration["MYSQL_URL"];

        var db = configuration.GetSection("Database");
        yield return db["ConnectionString"];
        yield return configuration["Database__ConnectionString"];
        yield return configuration.GetConnectionString("Default");
        yield return configuration.GetConnectionString("MySQL");
        yield return configuration["ConnectionStrings__Default"];
        yield return configuration["ConnectionStrings__MySQL"];
        yield return configuration["MYSQL_CONNECTION_STRING"];
        yield return configuration["MYSQLCONNSTR_Default"];
    }

    private static string? NormalizeDirectConnectionString(string raw)
    {
        if (raw.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase))
            return TryParseMySqlUri(raw);

        try
        {
            var builder = new MySqlConnectionStringBuilder(raw);
            if (string.IsNullOrWhiteSpace(builder.Server) || string.IsNullOrWhiteSpace(builder.Database))
                return null;

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? TryParseMySqlUri(string uriRaw)
    {
        if (!Uri.TryCreate(uriRaw, UriKind.Absolute, out var uri))
            return null;

        if (!string.Equals(uri.Scheme, "mysql", StringComparison.OrdinalIgnoreCase))
            return null;

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        if (string.IsNullOrWhiteSpace(user))
            return null;

        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
            return null;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Database = database,
            UserID = user,
            Password = password,
        };

        if (uri.Port > 0)
            builder.Port = (uint)uri.Port;

        ApplyQueryParameters(uri.Query, builder);
        return builder.ConnectionString;
    }

    private static void ApplyQueryParameters(string query, MySqlConnectionStringBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        var trimmed = query.TrimStart('?');
        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = Uri.UnescapeDataString(segment[..eq]);
            var value = Uri.UnescapeDataString(segment[(eq + 1)..]);

            if (key.Equals("ssl-mode", StringComparison.OrdinalIgnoreCase)
                || key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<MySqlSslMode>(value, ignoreCase: true, out var ssl))
                    builder.SslMode = ssl;
            }
        }
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
                configuration["MYSQL_USERNAME"],
                configuration["DATABASE_USER"],
                configuration["DATABASE_USERNAME"],
                configuration["DB_USER"],
                configuration["DB_USERNAME"]),
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
