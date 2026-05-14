using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class FnoMultiHorizonFeaturePipelineTests
{
    [Fact]
    public void DetectIntervalProfileKey_MapsCommonIntervals()
    {
        var oneMinute = BuildCandles(70, minutesStep: 1);
        var fiveMinute = BuildCandles(70, minutesStep: 5);
        var fifteenMinute = BuildCandles(70, minutesStep: 15);

        Assert.Equal("1m", FnoMultiHorizonFeaturePipeline.DetectIntervalProfileKey(oneMinute));
        Assert.Equal("5m", FnoMultiHorizonFeaturePipeline.DetectIntervalProfileKey(fiveMinute));
        Assert.Equal("15m", FnoMultiHorizonFeaturePipeline.DetectIntervalProfileKey(fifteenMinute));
    }

    [Fact]
    public void TryExtractFeatures_ProducesFiniteFixedLengthVector()
    {
        var candles = BuildCandles(90, minutesStep: 1);
        Assert.True(FnoMultiHorizonFeaturePipeline.TryExtractFeatures(candles, 80, out var f));
        Assert.Equal(FnoMultiHorizonFeaturePipeline.FeatureCount, f.Length);
        Assert.All(f, v => Assert.True(float.IsFinite(v)));
    }

    [Fact]
    public void TryBuildTrainingSet_BuildsBinaryRows()
    {
        var candles = BuildCandles(120, minutesStep: 5);
        Assert.True(FnoMultiHorizonFeaturePipeline.TryBuildTrainingSet(candles, 0m, out var rows));
        Assert.True(rows.Count > 20);
    }

    private static List<KiteHistoricalCandlePointDto> BuildCandles(int count, int minutesStep)
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T09:15:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var outRows = new List<KiteHistoricalCandlePointDto>(count);
        var close = 100m;
        for (var i = 0; i < count; i++)
        {
            var wave = (decimal)Math.Sin(i / 9.0) * 0.35m;
            var drift = 0.04m + ((i % 7 == 0) ? 0.08m : 0m);
            var open = close;
            close = Math.Max(1m, close + drift + wave);
            var high = Math.Max(open, close) + 0.12m;
            var low = Math.Min(open, close) - 0.11m;
            var vol = 1000 + (i % 11) * 30;
            var ema9 = close * 0.998m;
            var ema21 = close * 0.996m;
            var srSupport = close * 0.992m;
            var srResistance = close * 1.008m;
            outRows.Add(new KiteHistoricalCandlePointDto(
                t0.AddMinutes(i * minutesStep),
                open,
                high,
                low,
                close,
                vol,
                Sma20: null,
                Ema9: ema9,
                Ema21: ema21,
                SrSupport: srSupport,
                SrResistance: srResistance));
        }

        return outRows;
    }
}

