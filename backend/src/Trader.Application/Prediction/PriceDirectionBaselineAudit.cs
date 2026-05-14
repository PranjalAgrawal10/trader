using Trader.Application.Broker;

namespace Trader.Application.Prediction;

/// <summary>
/// Repository of baseline model feature coverage so evaluations can compare newer engines against the current stack.
/// </summary>
public static class PriceDirectionBaselineAudit
{
    public static readonly IReadOnlyDictionary<string, string[]> FeatureCoverageByModelId =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["mlnet-sdca-logistic-v1"] =
            [
                "close_return_lag_1",
                "close_return_lag_2",
                "close_return_lag_3",
                "close_return_lag_5",
                "close_return_lag_10",
                "sma5_minus_sma10_over_sma10",
            ],
            ["mlnet-lightgbm-triple-barrier-v1"] =
            [
                "close_return_lag_1",
                "close_return_lag_2",
                "close_return_lag_3",
                "close_return_lag_5",
                "close_return_lag_10",
                "close_minus_sma5_over_sma5",
                "close_minus_sma10_over_sma10",
                "candle_body_pct",
                "candle_range_pct",
                "candle_close_position_in_range",
                "volume_over_sma10_volume",
                "distance_to_sr_support",
                "distance_to_sr_resistance",
                "close_minus_ema9_over_ema9",
                "close_minus_ema21_over_ema21",
                "close_minus_rolling_typical_vwap_over_vwap",
            ],
            ["momentum-close-v1"] =
            [
                "last_close_vs_previous_close_sign",
            ],
        };

    public static readonly IReadOnlyDictionary<string, string> BaselineMetricFocusByModelId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mlnet-sdca-logistic-v1"] = "Short-horizon next-bar direction with SDCA logistic confidence.",
            ["mlnet-lightgbm-triple-barrier-v1"] = "Barrier-hit classification quality with confidence abstain around 50%.",
            ["momentum-close-v1"] = "Simple directional sign benchmark for fallback and sanity checks.",
        };

    /// <summary>
    /// Returns baseline label by interval key (1m / 5m / 15m / fallback) from the current threshold settings.
    /// </summary>
    public static sbyte? BaselineSignedLabelForNextBar(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        int currentIndex,
        decimal thresholdFraction)
    {
        if (currentIndex < 0 || currentIndex + 1 >= candles.Count)
            return null;
        return PriceDirectionLabeling.ClassifySignedLabel(
            candles[currentIndex].Close,
            candles[currentIndex + 1].Close,
            thresholdFraction);
    }
}

