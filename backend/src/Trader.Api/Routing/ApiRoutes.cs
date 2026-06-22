namespace Trader.Api.Routing;

/// <summary>Central route templates for the HTTP API and SignalR hubs.</summary>
public static class ApiRoutes
{
    public const string Version = "1.0";
    public const string SwaggerGroup = "v1";

    /// <summary><c>api/v{version:apiVersion}</c></summary>
    public const string VersionedPrefix = "api/v{version:apiVersion}";

    public const string Health = "/health";
    public const string ApiHealth = "/api/health";
    public const string Root = "/";
    public const string Swagger = "/swagger";
    public const string ApiV1Root = "/api/v1";

    /// <summary>Realtime market ticks (see <see cref="Hubs.MarketHub"/>).</summary>
    public const string HubsMarket = "/hubs/market";

    public static string V1(string segment) => $"{VersionedPrefix}/{segment}";
}
