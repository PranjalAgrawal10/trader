namespace Trader.Application.Broker;

/// <summary>
/// SMA / EMA on close and swing support / resistance (matches Trader SPA <c>movingAverages.ts</c>) for Kite historical chart overlays.
/// </summary>
internal static class ChartMovingAverages
{
    public const int SmaPeriod = 20;
    public const int EmaFastPeriod = 9;
    public const int EmaSlowPeriod = 21;

    /// <summary>Rolling window for min-low (support) / max-high (resistance); aligned with <see cref="SmaPeriod"/>.</summary>
    public const int SwingSupportResistancePeriod = SmaPeriod;

    /// <summary>Extra bars requested before the visible window so the first candle can show full EMA/SMA lines.</summary>
    public const int WarmupBarCount = 120;

    public static IReadOnlyList<KiteHistoricalCandlePointDto> Attach(
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc)
    {
        if (candlesAsc.Count == 0)
            return candlesAsc;

        var closes = candlesAsc.Select(c => c.Close).ToList();
        var sma = ComputeSma(closes, SmaPeriod);
        var ema9 = ComputeEma(closes, EmaFastPeriod);
        var ema21 = ComputeEma(closes, EmaSlowPeriod);
        var (support, resistance) = ComputeSwingSupportResistance(candlesAsc, SwingSupportResistancePeriod);

        var list = new List<KiteHistoricalCandlePointDto>(candlesAsc.Count);
        for (var i = 0; i < candlesAsc.Count; i++)
        {
            var c = candlesAsc[i];
            list.Add(new KiteHistoricalCandlePointDto(
                c.Time,
                c.Open,
                c.High,
                c.Low,
                c.Close,
                c.Volume,
                sma[i],
                ema9[i],
                ema21[i],
                support[i],
                resistance[i]));
        }

        return list;
    }

    private static (decimal?[] Support, decimal?[] Resistance) ComputeSwingSupportResistance(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        int period)
    {
        var n = candles.Count;
        var support = new decimal?[n];
        var resistance = new decimal?[n];
        if (period < 1 || n == 0)
            return (support, resistance);

        for (var i = period - 1; i < n; i++)
        {
            var minL = decimal.MaxValue;
            var maxH = decimal.MinValue;
            for (var j = i - period + 1; j <= i; j++)
            {
                minL = Math.Min(minL, candles[j].Low);
                maxH = Math.Max(maxH, candles[j].High);
            }

            support[i] = minL;
            resistance[i] = maxH;
        }

        return (support, resistance);
    }

    private static IReadOnlyList<decimal?> ComputeSma(IReadOnlyList<decimal> values, int period)
    {
        var n = values.Count;
        var outArr = new decimal?[n];
        if (period < 1 || n < period)
            return outArr;

        for (var i = period - 1; i < n; i++)
        {
            decimal s = 0;
            for (var j = 0; j < period; j++)
                s += values[i - j];
            outArr[i] = s / period;
        }

        return outArr;
    }

    /// <summary>EMA seeded with SMA at index <c>period - 1</c>, then standard smoothing (matches frontend).</summary>
    private static IReadOnlyList<decimal?> ComputeEma(IReadOnlyList<decimal> values, int period)
    {
        var n = values.Count;
        var outArr = new decimal?[n];
        if (period < 1 || n < period)
            return outArr;

        decimal ema = 0;
        for (var i = 0; i < period; i++)
            ema += values[i];
        ema /= period;

        var k = 2m / (period + 1m);
        outArr[period - 1] = ema;

        for (var i = period; i < n; i++)
        {
            ema = values[i] * k + ema * (1m - k);
            outArr[i] = ema;
        }

        return outArr;
    }
}
