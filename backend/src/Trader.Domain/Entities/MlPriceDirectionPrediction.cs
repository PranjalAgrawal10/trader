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
    public string ModelId { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    /// <summary><c>pending</c>, <c>correct</c>, or <c>wrong</c>.</summary>
    public string Outcome { get; set; } = "pending";

    public DateTimeOffset? NextBarTimeUtc { get; set; }
    public decimal? NextClose { get; set; }
}
