namespace Trader.Domain.Entities;

/// <summary>
/// Logged predictions from the triple-barrier–style LightGBM pipeline, stored separately from <see cref="MlPriceDirectionPrediction"/>.
/// </summary>
public class MlLightGbmTripleBarrierPrediction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string InstrumentToken { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;

    public DateTimeOffset PredictedAtUtc { get; set; }
    public DateTimeOffset RefBarTimeUtc { get; set; }
    public decimal RefClose { get; set; }

    /// <summary><c>up</c>, <c>down</c>, or <c>neutral</c>.</summary>
    public string Direction { get; set; } = string.Empty;

    public int Confidence { get; set; }

    /// <summary>Registered triple-barrier LightGBM engine id (constant in application configuration).</summary>
    public string? EngineModelId { get; set; }

    /// <summary>Often <c>mlnet-lightgbm-triple-barrier-v1</c>; may differ when the engine falls back to a heuristic scorer.</summary>
    public string ModelId { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    /// <summary><c>pending</c>, <c>correct</c>, or <c>wrong</c>.</summary>
    public string Outcome { get; set; } = "pending";

    /// <summary>Optional origin, e.g. <c>automation</c> for favorite auto-predict job.</summary>
    public string? Source { get; set; }

    public DateTimeOffset? NextBarTimeUtc { get; set; }
    public decimal? NextClose { get; set; }
}
