using System.Text.Json;
using Trader.Application.Auth;

namespace Trader.Api.Extensions;

public static class HttpRequestAuthContextExtensions
{
    public static AuthRequestContext BuildAuthRequestContext(this HttpRequest request)
    {
        var remoteIp = request.HttpContext.Connection.RemoteIpAddress?.ToString();
        var forwardedFor = HeaderOrNull(request, "X-Forwarded-For");
        var userAgent = HeaderOrNull(request, "User-Agent");

        var ipInfo = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["remote_ip"] = remoteIp,
            ["x_forwarded_for"] = forwardedFor,
            ["x_real_ip"] = HeaderOrNull(request, "X-Real-IP"),
            ["cf_connecting_ip"] = HeaderOrNull(request, "CF-Connecting-IP"),
            ["x_client_ip"] = HeaderOrNull(request, "X-Client-IP"),
            ["cf_ip_country"] = HeaderOrNull(request, "CF-IPCountry"),
            ["x_forwarded_proto"] = HeaderOrNull(request, "X-Forwarded-Proto"),
        };

        return new AuthRequestContext(
            remoteIp,
            forwardedFor,
            userAgent,
            JsonSerializer.Serialize(ipInfo));
    }

    private static string? HeaderOrNull(HttpRequest request, string name)
    {
        if (!request.Headers.TryGetValue(name, out var value))
            return null;

        var raw = value.ToString().Trim();
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}
