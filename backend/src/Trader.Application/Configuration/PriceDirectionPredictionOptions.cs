namespace Trader.Application.Configuration;

/// <summary>
/// Pick which registered price-direction engine runs when the client omits an explicit model id.
/// </summary>
public sealed class PriceDirectionPredictionOptions
{
    public const string SectionName = "PriceDirectionPrediction";

    /// <summary>
    /// Must match a registered engine <c>ModelId</c> (see Infrastructure DI registration).
    /// </summary>
    public string DefaultModelId { get; set; } = "mlnet-sdca-logistic-v1";

    /// <summary>
    /// Symmetric fractional band around flat returns treated as labeled <c>neutral</c>.
    /// Example: 0.0005 means moves within ±0.05% vs the reference close are labeled neutral when resolving outcomes and persisting labels.
    /// </summary>
    public decimal LabelThresholdFraction { get; set; }

    /// <summary>
    /// Optional per-interval label thresholds (keys like <c>1m</c>, <c>5m</c>, <c>15m</c>).
    /// Used by multi-horizon engines; falls back to <see cref="LabelThresholdFraction"/>.
    /// </summary>
    public Dictionary<string, decimal> LabelThresholdFractionByInterval { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional JSON file with piecewise-linear isotonic calibration of raw P(up) from the SDCA pipeline.
    /// Schema is <c>{{ "xs": [...], "ys": [...] }}</c> monotone non-decreasing xs in [0,1]; ys in [0,1].
    /// May be absolute or app-relative; see <see cref="JsonPiecewiseProbabilityCalibrator"/>.
    /// </summary>
    public string? ScoreCalibrationJsonPath { get; set; }

    /// <summary>
    /// Optional per-interval calibration JSON map paths (<c>{ "1m": "path-a.json", "5m": "path-b.json" }</c>).
    /// Missing keys fall back to <see cref="ScoreCalibrationJsonPath"/>.
    /// </summary>
    public Dictionary<string, string> ScoreCalibrationJsonPathByInterval { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Minimum training rows by inferred interval profile key (for example 1m / 5m / 15m).
    /// Missing keys use engine defaults.
    /// </summary>
    public Dictionary<string, int> MinTrainingRowsByInterval { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Confidence floor (0-100) below which model output is forced to neutral by interval profile key.
    /// Missing keys use engine defaults.
    /// </summary>
    public Dictionary<string, int> NeutralConfidenceFloorByInterval { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
