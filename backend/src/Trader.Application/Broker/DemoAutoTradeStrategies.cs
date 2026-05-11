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

    /// <summary>Weight ∝ confidence² across all directional legs (concentrates on strong model scores).</summary>
    public const string SignalStrengthSquared = "signal_strength_squared";

    /// <summary>Weight ∝ max(0, 2p−1) with p = confidence/100 (fractional “edge” sizing; p≤50 → zero weight).</summary>
    public const string ImpliedEdgeWeighted = "implied_edge_weighted";

    /// <summary>One leg per <c>engineModelId</c> (highest confidence), then equal notional across those legs.</summary>
    public const string OneSignalPerEngine = "one_signal_per_engine";

    /// <summary>Keep only the top ⌈n/2⌉ directional legs by confidence, then equal split.</summary>
    public const string TopHalfConfidence = "top_half_confidence";

    public static readonly IReadOnlyList<string> All = new[]
    {
        EqualSplit,
        ConfidenceWeighted,
        HighConviction,
        OneSignalPerInstrument,
        SignalStrengthSquared,
        ImpliedEdgeWeighted,
        OneSignalPerEngine,
        TopHalfConfidence,
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
            SignalStrengthSquared => (
                SignalStrengthSquared,
                "Quadratic confidence",
                "Weights each directional leg by confidence squared (then normalizes to the demo notional)—emphasizes top scores vs linear weighting."),
            ImpliedEdgeWeighted => (
                ImpliedEdgeWeighted,
                "Implied edge (fractional)",
                "Treats confidence/100 as a crude win-probability proxy; weights ∝ max(0, 2p−1) (zero below 50%), then normalizes—akin to a toy fractional-Kelly slice."),
            OneSignalPerEngine => (
                OneSignalPerEngine,
                "One leg per engine",
                "For each registered engine id, keeps the single highest-confidence directional signal, then splits notional evenly across engines."),
            TopHalfConfidence => (
                TopHalfConfidence,
                "Top half by confidence",
                "Keeps only the upper half (⌈n/2⌉) of directional legs ranked by confidence, then splits the full notional evenly among survivors."),
            _ => (
                EqualSplit,
                "Equal risk per signal",
                "Splits the full notional evenly across every directional signal that has a resolved next close (neutral ignored)."),
        };
}
