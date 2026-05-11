using Trader.Application.Broker;

namespace Trader.Application.Prediction;

/// <summary>
/// Validates merged <strong>m</strong>-minute (or other UI) OHLCV used for favorite automation before invoking engines.
/// </summary>
public static class FavoriteMlAutomationCandleValidator
{
    /// <summary>
    /// Returns false when the series is unusable for ML (count, monotonic time, basic OHLC invariants).
    /// </summary>
    public static bool IsValidForPrediction(IReadOnlyList<KiteHistoricalCandlePointDto> candles, out string? reason)
    {
        reason = null;
        if (candles.Count < PriceDirectionPredictionService.MinCandlesRequired)
        {
            reason = $"need at least {PriceDirectionPredictionService.MinCandlesRequired} bars; got {candles.Count}";
            return false;
        }

        for (var i = 0; i < candles.Count; i++)
        {
            if (!HasValidOhlc(candles[i], i, out var r))
            {
                reason = r;
                return false;
            }
        }

        for (var i = 1; i < candles.Count; i++)
        {
            if (candles[i].Time <= candles[i - 1].Time)
            {
                reason = $"bar times not strictly increasing at index {i}";
                return false;
            }
        }

        return true;
    }

    private static bool HasValidOhlc(KiteHistoricalCandlePointDto c, int index, out string? reason)
    {
        if (c.High < c.Low)
        {
            reason = $"bar {index}: high < low";
            return false;
        }

        if (c.High < c.Open || c.High < c.Close)
        {
            reason = $"bar {index}: high below open or close";
            return false;
        }

        if (c.Low > c.Open || c.Low > c.Close)
        {
            reason = $"bar {index}: low above open or close";
            return false;
        }

        if (c.Volume < 0)
        {
            reason = $"bar {index}: negative volume";
            return false;
        }

        reason = null;
        return true;
    }
}
