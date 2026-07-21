namespace Trader.Application.Broker;

/// <summary>Supported underlyings for Opening ATM (09:15 IST live MIS).</summary>
public static class OpeningAtmUnderlyings
{
    public sealed record Spec(
        string Key,
        string Label,
        string SpotSearchQuery,
        string OptionSearchQuery,
        string PreferredSpotExchange,
        /// <summary>Normalized spot tradingsymbol/name match (no spaces), e.g. NIFTY50.</summary>
        string SpotSymbolNorm,
        /// <summary>Must appear in option symbol/name (no spaces).</summary>
        string OptionIncludeNorm,
        /// <summary>Substrings that disqualify a row even if include matches (e.g. BANKNIFTY vs NIFTY).</summary>
        IReadOnlyList<string> OptionExcludeNorms);

    public static readonly IReadOnlyList<Spec> All = new[]
    {
        new Spec(
            "nifty",
            "NIFTY",
            "NIFTY 50",
            "NIFTY",
            "NSE",
            "NIFTY50",
            "NIFTY",
            new[] { "BANKNIFTY", "FINNIFTY", "MIDCPNIFTY" }),
        new Spec(
            "banknifty",
            "BANKNIFTY",
            "NIFTY BANK",
            "BANKNIFTY",
            "NSE",
            "NIFTYBANK",
            "BANKNIFTY",
            Array.Empty<string>()),
        new Spec(
            "finnifty",
            "FINNIFTY",
            "NIFTY FIN SERVICE",
            "FINNIFTY",
            "NSE",
            "NIFTYFINSERVICE",
            "FINNIFTY",
            Array.Empty<string>()),
        new Spec(
            "midcpnifty",
            "MIDCPNIFTY",
            "NIFTY MID SELECT",
            "MIDCPNIFTY",
            "NSE",
            "NIFTYMIDSELECT",
            "MIDCPNIFTY",
            Array.Empty<string>()),
        new Spec(
            "sensex",
            "SENSEX",
            "SENSEX",
            "SENSEX",
            "BSE",
            "SENSEX",
            "SENSEX",
            Array.Empty<string>()),
        new Spec(
            "bankex",
            "BANKEX",
            "BANKEX",
            "BANKEX",
            "BSE",
            "BANKEX",
            "BANKEX",
            Array.Empty<string>()),
    };

    public const string DefaultKey = "nifty";

    public static Spec Resolve(string? key)
    {
        var k = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (k.Length == 0)
            k = DefaultKey;
        return All.FirstOrDefault(x => x.Key == k) ?? All[0]!;
    }

    public static bool IsKnown(string? key) =>
        All.Any(x => string.Equals(x.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}
