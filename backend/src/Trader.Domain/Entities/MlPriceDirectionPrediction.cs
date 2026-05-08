namespace Trader.Domain.Entities;

/// <summary>Logged ML next-bar direction prediction for a Kite instrument and chart interval (per user).</summary>
public class MlPriceDirectionPrediction
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

    /// <summary>Registered price-direction engine model id (stable key from the engine registry).</summary>
    public string? EngineModelId { get; set; }

    public string ModelId { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    /// <summary><c>pending</c>, <c>correct</c>, or <c>wrong</c>.</summary>
    public string Outcome { get; set; } = "pending";

    /// <summary>Optional origin, e.g. <c>automation</c> for favorite auto-predict job; null/empty = interactive API/SPA.</summary>
    public string? Source { get; set; }

    public DateTimeOffset? NextBarTimeUtc { get; set; }
    public decimal? NextClose { get; set; }

    /// <summary>Threshold applied when this row was created (for reproducible labeling).</summary>
    public decimal? LabelThresholdFractionApplied { get; set; }

    /// <summary>e.g. <c>session_end</c>, <c>gap_too_large</c>; training pipelines should exclude when set.</summary>
    public string? CensorReason { get; set; }

    /// <summary>Signed thresholded label vs next close: +1 up, −1 down, 0 neutral.</summary>
    public sbyte? LabelNextBar { get; set; }

    public sbyte? LabelN3 { get; set; }
    public sbyte? LabelN5 { get; set; }

    public DateTimeOffset? NextBarTimeUtcN3 { get; set; }
    public decimal? NextCloseN3 { get; set; }
    public DateTimeOffset? NextBarTimeUtcN5 { get; set; }
    public decimal? NextCloseN5 { get; set; }
}
