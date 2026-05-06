namespace Trader.Application.Prediction;

public enum PriceDirectionLabel
{
    Up = 1,
    Down = 2,
    Neutral = 0,
}

/// <summary>Output of the price-direction ML layer (next-bar bias from recent closes).</summary>
public sealed record PriceDirectionResult(
    PriceDirectionLabel Direction,
    /// <summary>0–100 calibrated strength (not a guarantee of profit).</summary>
    int Confidence,
    string ModelId,
    string Detail);
