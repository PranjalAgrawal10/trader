using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class PriceDirectionWalkForwardEvaluatorTests
{
    private sealed class MomentumLikeEngine : IPriceDirectionPredictionEngine
    {
        public string ModelId => "momentum-like";
        public string Description => "test";

        public PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles)
        {
            var a = candles[^2].Close;
            var b = candles[^1].Close;
            if (b > a)
                return new PriceDirectionResult(PriceDirectionLabel.Up, 68, ModelId, "up");
            if (b < a)
                return new PriceDirectionResult(PriceDirectionLabel.Down, 68, ModelId, "down");
            return new PriceDirectionResult(PriceDirectionLabel.Neutral, 50, ModelId, "flat");
        }
    }

    private sealed class AlwaysNeutralEngine : IPriceDirectionPredictionEngine
    {
        public string ModelId => "neutral";
        public string Description => "test";

        public PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles) =>
            new(PriceDirectionLabel.Neutral, 50, ModelId, "neutral");
    }

    [Fact]
    public void Evaluate_ProducesMetricsWithFiniteFields()
    {
        var candles = BuildTrendCandles(140, minutesStep: 1);
        var metrics = PriceDirectionWalkForwardEvaluator.Evaluate(candles, new MomentumLikeEngine(), thresholdFraction: 0m);
        Assert.True(metrics.Evaluated > 0);
        Assert.True(double.IsFinite(metrics.Accuracy));
        Assert.True(double.IsFinite(metrics.BalancedAccuracy));
        Assert.True(double.IsFinite(metrics.BrierScore));
    }

    [Fact]
    public void PromotionGate_RejectsWeakCandidate()
    {
        var candles = BuildTrendCandles(180, minutesStep: 5);
        var baseline = PriceDirectionWalkForwardEvaluator.Evaluate(candles, new MomentumLikeEngine(), 0m);
        var weak = PriceDirectionWalkForwardEvaluator.Evaluate(candles, new AlwaysNeutralEngine(), 0m);
        var decision = PriceDirectionPromotionGateEvaluator.Evaluate(
            weak,
            baseline,
            new PriceDirectionPromotionGate(
                MinAccuracyLift: 0.0,
                MinBalancedAccuracyLift: 0.0,
                MaxBrierScoreWorsening: 0.0,
                MinEvaluatedRows: 20));
        Assert.False(decision.Accepted);
    }

    private static List<KiteHistoricalCandlePointDto> BuildTrendCandles(int count, int minutesStep)
    {
        var t0 = DateTimeOffset.Parse("2026-01-08T09:15:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var rows = new List<KiteHistoricalCandlePointDto>(count);
        var close = 100m;
        for (var i = 0; i < count; i++)
        {
            var drift = (i % 9 is 0 or 1) ? 0.20m : 0.05m;
            var noise = (decimal)Math.Sin(i / 5.0) * 0.03m;
            var open = close;
            close = Math.Max(1m, close + drift + noise);
            rows.Add(new KiteHistoricalCandlePointDto(
                t0.AddMinutes(i * minutesStep),
                open,
                Math.Max(open, close) + 0.08m,
                Math.Min(open, close) - 0.08m,
                close,
                1100 + (i % 10) * 25));
        }
        return rows;
    }
}

