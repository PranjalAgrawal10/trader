using Trader.Application.Broker;

namespace Trader.Application.Prediction;

/// <summary>
/// Encapsulates an ML (or pluggable) model that scores the <strong>latest</strong> candle for a directional bias,
/// trained on ascending historical OHLC (and backend-computed overlays such as MAs/SR when present).
/// </summary>
public interface IPriceDirectionPredictionEngine
{
    /// <summary>Stable key stored on predictions and used in the API <c>model</c> query (e.g. <c>mlnet-sdca-logistic-v1</c>).</summary>
    string ModelId { get; }

    /// <summary>Short human-readable summary for model pickers.</summary>
    string Description { get; }

    /// <summary>
    /// Trains a compact classifier over past bars where applicable and scores the <strong>last</strong> candle.
    /// </summary>
    PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles);
}
