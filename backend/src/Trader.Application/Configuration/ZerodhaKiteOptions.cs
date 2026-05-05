namespace Trader.Application.Configuration;

/// <summary>
/// Zerodha Kite Connect app settings. Supply values with environment variables
/// (<c>ZerodhaKite__ApiKey</c>, <c>ZerodhaKite__ApiSecret</c>, <c>ZerodhaKite__RedirectUrl</c>, etc.)
/// or, in Development only, the same keys in <c>.env.development</c> / <c>.env.development.local</c>
/// (see <c>DotEnvBootstrap</c>). Do not put API secrets in committed <c>appsettings*.json</c>.
/// </summary>
public sealed class ZerodhaKiteOptions
{
    public const string SectionName = "ZerodhaKite";

    /// <summary>Kite Connect app API key (public).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Kite Connect app API secret (server-only).</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// Exact redirect URL registered in the Kite developer console (e.g.
    /// https://localhost:5232/api/v1/broker/kite/callback).
    /// </summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>Browser URL after OAuth completes (usually your SPA).</summary>
    public string PostLoginRedirectUrl { get; set; } = "http://localhost:5173/brokers";
}
