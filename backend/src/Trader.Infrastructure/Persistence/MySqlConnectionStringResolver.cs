using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Trader.Infrastructure.Persistence;

/// <summary>
/// Resolves the MySQL connection string from <c>ConnectionStrings:MySQL</c>, <c>DATABASE_URL</c> (<c>mysql://</c>),
/// or discrete <c>Database:*</c> fields. DigitalOcean and other hosts often supply <c>mysql://user:pass@host:port/db?ssl-mode=REQUIRED</c>,
/// which is not valid for <see cref="MySqlConnectionStringBuilder"/> until converted.
/// </summary>
public static class MySqlConnectionStringResolver
{
    public static string? Resolve(IConfiguration configuration)
    {
        var candidate = FirstNonEmpty(
            configuration.GetConnectionString("MySQL"),
            configuration.GetConnectionString("DefaultConnection"),
            configuration["DATABASE_URL"]);

        candidate = NormalizeQuotedOrWrapped(candidate);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            if (LooksLikeMySqlUri(candidate))
                return ConvertMySqlUriToAdoNet(candidate);

            TryValidateAdoNetOrThrow(candidate);
            return candidate;
        }

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

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        return null;
    }

    /// <summary>
    /// Trims and removes a single pair of surrounding quotes (common when secrets are pasted with quotes).
    /// </summary>
    private static string? NormalizeQuotedOrWrapped(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var s = value.Trim();
        if (s.Length >= 2
            && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static bool LooksLikeMySqlUri(string s)
    {
        return s.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("jdbc:mysql://", StringComparison.OrdinalIgnoreCase);
    }

    private static string ConvertMySqlUriToAdoNet(string uriText)
    {
        var text = uriText.Trim();
        if (text.StartsWith("jdbc:mysql://", StringComparison.OrdinalIgnoreCase))
            text = "mysql://" + text["jdbc:mysql://".Length..];

        if (!text.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase))
            throw BuildHelpfulFormatException(uriText);

        var remainder = text["mysql://".Length..];
        var at = remainder.LastIndexOf('@');
        if (at < 0)
            throw BuildHelpfulFormatException(uriText, "Expected user:password@host in mysql:// URL.");

        var userPass = remainder[..at];
        var hostDbQuery = remainder[(at + 1)..];

        var userColon = userPass.IndexOf(':');
        var user = Uri.UnescapeDataString(userColon >= 0 ? userPass[..userColon] : userPass);
        var password = userColon >= 0 ? Uri.UnescapeDataString(userPass[(userColon + 1)..]) : string.Empty;

        var slash = hostDbQuery.IndexOf('/');
        var hostPort = slash >= 0 ? hostDbQuery[..slash] : hostDbQuery;
        var pathAndQuery = slash >= 0 ? hostDbQuery[slash..] : string.Empty;

        ParseHostPort(hostPort, out var host, out var port);

        var pathQuery = pathAndQuery.TrimStart('/');
        var qIdx = pathQuery.IndexOf('?', StringComparison.Ordinal);
        var database = qIdx >= 0 ? pathQuery[..qIdx] : pathQuery;
        var query = qIdx >= 0 ? pathQuery[qIdx..] : string.Empty;

        if (string.IsNullOrWhiteSpace(host))
            throw BuildHelpfulFormatException(uriText, "Host is missing in mysql:// URL.");
        if (string.IsNullOrWhiteSpace(database))
            throw BuildHelpfulFormatException(uriText, "Database name (path segment) is missing in mysql:// URL.");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            UserID = user,
            Password = password,
            Database = database,
        };

        ApplyMySqlUriQuery(builder, query);
        return builder.ConnectionString;
    }

    private static void ParseHostPort(string hostPort, out string host, out uint port)
    {
        port = 3306;
        var trimmed = hostPort.Trim();
        host = trimmed;
        if (string.IsNullOrEmpty(trimmed))
            return;

        if (trimmed.StartsWith('['))
        {
            var endBracket = trimmed.IndexOf(']', 1);
            if (endBracket > 0)
            {
                host = trimmed[1..endBracket];
                if (endBracket + 1 < trimmed.Length && trimmed[endBracket + 1] == ':'
                    && uint.TryParse(trimmed[(endBracket + 2)..], out var ipv6Port))
                    port = ipv6Port;
            }

            return;
        }

        var colon = trimmed.LastIndexOf(':');
        if (colon > 0 && colon < trimmed.Length - 1 && uint.TryParse(trimmed[(colon + 1)..], out var p))
        {
            host = trimmed[..colon];
            port = p;
        }
    }

    private static void ApplyMySqlUriQuery(MySqlConnectionStringBuilder builder, string queryWithQuestionOrEmpty)
    {
        var q = queryWithQuestionOrEmpty.TrimStart('?');
        if (string.IsNullOrEmpty(q))
            return;

        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            var key = (eq >= 0 ? part[..eq] : part).ToLowerInvariant();
            var val = eq >= 0 ? Uri.UnescapeDataString(part[(eq + 1)..]) : string.Empty;

            switch (key)
            {
                case "ssl-mode":
                case "sslmode":
                    if (Enum.TryParse<MySqlSslMode>(val.Replace('-', '_'), ignoreCase: true, out var ssl))
                        builder.SslMode = ssl;
                    else if (string.Equals(val, "REQUIRED", StringComparison.OrdinalIgnoreCase))
                        builder.SslMode = MySqlSslMode.Required;
                    break;
            }
        }
    }

    private static void TryValidateAdoNetOrThrow(string connectionString)
    {
        try
        {
            _ = new MySqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw BuildHelpfulFormatException(connectionString, inner: ex);
        }
    }

    private static InvalidOperationException BuildHelpfulFormatException(
        string? badValue,
        string? detail = null,
        ArgumentException? inner = null)
    {
        var prefix = badValue == null || badValue.Length == 0
            ? "(empty)"
            : $"{badValue[0]}… (length {badValue.Length})";

        var msg =
            "MySQL configuration is not a valid ADO.NET connection string. " +
            "Use **Server=…;Database=…;User Id=…;Password=…;Port=…;SslMode=Required;** (Pomelo style), " +
            "or **DATABASE_URL** / **ConnectionStrings__MySQL** as **mysql://user:pass@host:port/database?ssl-mode=REQUIRED**. " +
            "Do **not** wrap the value in extra JSON or quotes in the platform UI. " +
            $"Problem value starts with `{prefix}`.";
        if (!string.IsNullOrEmpty(detail))
            msg += " " + detail;

        return inner == null
            ? new InvalidOperationException(msg)
            : new InvalidOperationException(msg, inner);
    }
}
