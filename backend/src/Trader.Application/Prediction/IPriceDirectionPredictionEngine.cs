namespace Trader.Application.Prediction;

/// <summary>
/// Encapsulates an ML (or pluggable) model that estimates whether the <strong>next</strong> close is biased up or down
/// from the latest bar, using only past closes.
/// </summary>
public interface IPriceDirectionPredictionEngine
{
    /// <summary>Stable key stored on predictions and used in the API <c>model</c> query (e.g. <c>mlnet-sdca-logistic-v1</c>).</summary>
    string ModelId { get; }

    /// <summary>Short human-readable summary for model pickers.</summary>
    string Description { get; }

    /// <summary>
    /// Trains a small binary classifier on the supplied series and scores the <strong>last</strong> observation.
    /// </summary>
    PriceDirectionResult PredictNextDirection(IReadOnlyList<decimal> closes);
}
