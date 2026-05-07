using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Prediction;

/// <summary>
/// Lightweight baseline: compares the last close to the prior bar (same idea as the ML.NET heuristic fallback, but as a first-class model).
/// </summary>
public sealed class MomentumPriceDirectionPredictionEngine : IPriceDirectionPredictionEngine
{
    public string ModelId => "momentum-close-v1";

    public string Description => "Baseline: last close vs prior bar (no ML).";

    public PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles)
    {
        if (candles.Count < PriceDirectionPredictionService.MinCandlesRequired)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Neutral,
                0,
                ModelId,
                $"Need at least {PriceDirectionPredictionService.MinCandlesRequired} candles; got {candles.Count}.");
        }

        var a = candles[^2].Close;
        var b = candles[^1].Close;
        if (b > a)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Up,
                55,
                ModelId,
                "Prior bar rose into the last close — momentum-style bias.");
        }

        if (b < a)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Down,
                55,
                ModelId,
                "Prior bar fell into the last close — momentum-style bias.");
        }

        return new PriceDirectionResult(
            PriceDirectionLabel.Neutral,
            50,
            ModelId,
            "Last bar is flat vs prior.");
    }
}
