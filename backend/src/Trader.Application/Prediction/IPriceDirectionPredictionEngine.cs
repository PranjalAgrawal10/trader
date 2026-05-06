namespace Trader.Application.Prediction;

/// <summary>
/// Encapsulates an ML (or pluggable) model that estimates whether the <strong>next</strong> close is biased up or down
/// from the latest bar, using only past closes.
/// </summary>
public interface IPriceDirectionPredictionEngine
{
    /// <summary>
    /// Trains a small binary classifier on the supplied series and scores the <strong>last</strong> observation.
    /// </summary>
    PriceDirectionResult PredictNextDirection(IReadOnlyList<decimal> closes);
}
