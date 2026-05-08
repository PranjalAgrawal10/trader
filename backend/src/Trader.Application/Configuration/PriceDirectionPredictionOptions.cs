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
    /// Optional JSON file with piecewise-linear isotonic calibration of raw P(up) from the SDCA pipeline.
    /// Schema is <c>{{ "xs": [...], "ys": [...] }}</c> monotone non-decreasing xs in [0,1]; ys in [0,1].
    /// May be absolute or app-relative; see <see cref="JsonPiecewiseProbabilityCalibrator"/>.
    /// </summary>
    public string? ScoreCalibrationJsonPath { get; set; }
}
