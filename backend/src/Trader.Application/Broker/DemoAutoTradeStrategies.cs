namespace Trader.Application.Broker;

/// <summary>
/// Persisted IDs for hypothetical demo allocation modes (education only — not broker orders / not investment advice).
/// </summary>
public static class DemoAutoTradeStrategyIds
{
    public const string EqualSplit = "equal_split";
    public const string ConfidenceWeighted = "confidence_weighted";
    public const string HighConviction = "high_conviction";
    public const string OneSignalPerInstrument = "one_signal_per_instrument";

    public static readonly IReadOnlyList<string> All = new[]
    {
        EqualSplit,
        ConfidenceWeighted,
        HighConviction,
        OneSignalPerInstrument,
    };

    public static string NormalizeOrDefault(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return EqualSplit;

        var t = raw.Trim();
        foreach (var id in All)
        {
            if (string.Equals(t, id, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return EqualSplit;
    }

    /// <summary>Validates a client-provided slug; use when persisting demo strategy.</summary>
    /// <exception cref="InvalidOperationException">Unknown code.</exception>
    public static string ParseRequired(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return EqualSplit;

        var t = raw.Trim();
        foreach (var id in All)
        {
            if (string.Equals(t, id, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        throw new InvalidOperationException(
            $"Unknown demo strategy '{t}'. Allowed: {string.Join(", ", All)}.");
    }

    /// <returns>Slug, short title (UI), longer disclaimer line.</returns>
    public static (string Code, string Title, string Explanation) Describe(string normalizedId) =>
        normalizedId switch
        {
            ConfidenceWeighted => (
                ConfidenceWeighted,
                "Confidence-weighted",
                "Splits demo notional in proportion to each model’s confidence (1–100) across directional signals only."),
            HighConviction => (
                HighConviction,
                "High conviction (≥65%)",
                "Uses only directional signals with confidence ≥65%, then splits the full notional evenly across those legs."),
            OneSignalPerInstrument => (
                OneSignalPerInstrument,
                "One leg per instrument",
                "For each symbol, keeps the single highest-confidence directional signal (reduces duplicate engines), then splits notional evenly."),
            _ => (
                EqualSplit,
                "Equal risk per signal",
                "Splits the full notional evenly across every directional signal that has a resolved next close (neutral ignored)."),
        };
}
