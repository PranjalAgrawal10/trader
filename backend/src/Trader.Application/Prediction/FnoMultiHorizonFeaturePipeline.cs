using Trader.Application.Broker;

namespace Trader.Application.Prediction;

/// <summary>
/// Shared feature + label pipeline for F&amp;O direction models.
/// The interval profile is inferred from candle spacing so engines can keep the existing inference contract.
/// </summary>
public static class FnoMultiHorizonFeaturePipeline
{
    public const int FeatureCount = 24;
    public const int FirstFeatureIndex = 32;

    public static string DetectIntervalProfileKey(IReadOnlyList<KiteHistoricalCandlePointDto> candles)
    {
        if (candles.Count < 3)
            return "other";

        var deltas = new List<double>(Math.Min(candles.Count - 1, 48));
        var start = Math.Max(1, candles.Count - 48);
        for (var i = start; i < candles.Count; i++)
        {
            var d = (candles[i].Time - candles[i - 1].Time).TotalMinutes;
            if (d > 0.2 && d < 120)
                deltas.Add(d);
        }

        if (deltas.Count == 0)
            return "other";
        deltas.Sort();
        var median = deltas[deltas.Count / 2];
        if (median <= 1.5)
            return "1m";
        if (median <= 7.5)
            return "5m";
        if (median <= 20.0)
            return "15m";
        return "other";
    }

    public static bool TryBuildTrainingSet(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        decimal labelThresholdFraction,
        out List<(float[] Features, bool Label)> rows)
    {
        rows = new List<(float[] Features, bool Label)>();
        if (candles.Count < FirstFeatureIndex + 2)
            return false;

        var t = Math.Max(0m, labelThresholdFraction);
        for (var i = FirstFeatureIndex; i < candles.Count - 1; i++)
        {
            if (!TryExtractFeatures(candles, i, out var f))
                continue;
            var lbl = PriceDirectionLabeling.ClassifySignedLabel(candles[i].Close, candles[i + 1].Close, t);
            if (lbl is not (1 or -1))
                continue;
            rows.Add((f, lbl == 1));
        }

        return rows.Count > 0;
    }

    public static bool TryExtractFeatures(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        int i,
        out float[] features)
    {
        features = Array.Empty<float>();
        if (i < FirstFeatureIndex || i >= candles.Count)
            return false;

        var bar = candles[i];
        if (bar.Close == 0)
            return false;

        var close = bar.Close;
        var open = bar.Open;
        var high = bar.High;
        var low = bar.Low;
        var vol = Math.Max(bar.Volume, 0L);

        float Ret(int lag)
        {
            if (i - lag < 0)
                return 0f;
            var prev = candles[i - lag].Close;
            if (prev == 0)
                return 0f;
            return (float)((close - prev) / prev);
        }

        decimal SmaClose(int period)
        {
            decimal s = 0;
            var n = 0;
            for (var k = 0; k < period && i - k >= 0; k++)
            {
                s += candles[i - k].Close;
                n++;
            }
            return n == 0 ? close : s / n;
        }

        decimal SmaVolume(int period)
        {
            decimal s = 0;
            var n = 0;
            for (var k = 0; k < period && i - k >= 0; k++)
            {
                s += Math.Max(candles[i - k].Volume, 0L);
                n++;
            }
            return n == 0 ? 0 : s / n;
        }

        float DistOver(decimal anchor)
        {
            if (anchor == 0)
                return 0f;
            return (float)((close - anchor) / anchor);
        }

        var sma5 = SmaClose(5);
        var sma10 = SmaClose(10);
        var sma20 = SmaClose(20);

        var range = high - low;
        if (range == 0)
            range = Math.Abs(close) * 0.0000001m;
        var bodyPct = (float)((close - open) / close);
        var rangePct = (float)((high - low) / close);
        var closeInRangeCentered = (float)Math.Clamp((double)((close - low) / range), 0d, 1d) - 0.5f;

        var volAvg10 = SmaVolume(10);
        var volAvg20 = SmaVolume(20);
        var volRatio10 = volAvg10 <= 0 ? 1f : (float)((decimal)vol / volAvg10);
        var volRatio20 = volAvg20 <= 0 ? 1f : (float)((decimal)vol / volAvg20);

        var rollingVwap = RollingTypicalVwap(candles, i, 20);
        var rollingStd20 = RollingCloseReturnStdDev(candles, i, 20);
        var rollingStd10 = RollingCloseReturnStdDev(candles, i, 10);

        var ts = bar.Time;
        var minuteOfDay = ts.Hour * 60 + ts.Minute;
        var minuteFrac = minuteOfDay / 1440f;
        var sessionFrac = (float)Math.Clamp((minuteOfDay - 9 * 60 - 15) / (6.25 * 60), 0, 1);
        var minuteSin = (float)Math.Sin(2 * Math.PI * minuteFrac);
        var minuteCos = (float)Math.Cos(2 * Math.PI * minuteFrac);

        var ema9Gap = bar.Ema9 is { } e9 && e9 != 0 ? DistOver(e9) : DistOver(sma5);
        var ema21Gap = bar.Ema21 is { } e21 && e21 != 0 ? DistOver(e21) : DistOver(sma20);
        var supportGap = bar.SrSupport is { } sup && close != 0 ? (float)((close - sup) / close) : 0f;
        var resistanceGap = bar.SrResistance is { } res && close != 0 ? (float)((res - close) / close) : 0f;
        var slope5 = DistOver(candles[Math.Max(0, i - 5)].Close);
        var slope20 = DistOver(candles[Math.Max(0, i - 20)].Close);

        features =
        [
            Ret(1),
            Ret(2),
            Ret(3),
            Ret(5),
            Ret(8),
            Ret(10),
            Ret(15),
            DistOver(sma5),
            DistOver(sma10),
            DistOver(sma20),
            bodyPct,
            rangePct,
            closeInRangeCentered,
            volRatio10,
            volRatio20,
            rollingVwap == 0 ? 0f : DistOver(rollingVwap),
            rollingStd10,
            rollingStd20,
            ema9Gap,
            ema21Gap,
            supportGap,
            resistanceGap,
            slope5 + slope20 * 0.5f,
            minuteSin * 0.5f + minuteCos * 0.25f + sessionFrac * 0.25f,
        ];

        return features.Length == FeatureCount && features.All(NumberIsFinite);
    }

    private static bool NumberIsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    private static decimal RollingTypicalVwap(IReadOnlyList<KiteHistoricalCandlePointDto> c, int i, int window)
    {
        decimal numerator = 0;
        decimal denominator = 0;
        for (var k = 0; k < window && i - k >= 0; k++)
        {
            var bar = c[i - k];
            var typical = (bar.High + bar.Low + bar.Close) / 3m;
            var v = Math.Max(bar.Volume, 0L);
            numerator += typical * v;
            denominator += v;
        }
        return denominator == 0 ? c[i].Close : numerator / denominator;
    }

    private static float RollingCloseReturnStdDev(IReadOnlyList<KiteHistoricalCandlePointDto> candles, int iEnd, int lookback)
    {
        if (iEnd < lookback)
            return 0f;

        double sum = 0;
        double sumSq = 0;
        var n = 0;
        for (var i = iEnd - lookback + 1; i <= iEnd; i++)
        {
            var prev = (double)candles[i - 1].Close;
            var cur = (double)candles[i].Close;
            if (prev <= 0)
                continue;
            var r = (cur - prev) / prev;
            sum += r;
            sumSq += r * r;
            n++;
        }

        if (n <= 1)
            return 0f;
        var mean = sum / n;
        var variance = Math.Max(0, sumSq / n - mean * mean);
        return (float)Math.Sqrt(variance);
    }
}

