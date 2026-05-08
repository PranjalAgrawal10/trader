using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class PriceDirectionBestOfThreeTests
{
    private sealed class SequenceEngine : IPriceDirectionPredictionEngine
    {
        private readonly Queue<PriceDirectionResult> _q;

        public SequenceEngine(params PriceDirectionResult[] results) =>
            _q = new Queue<PriceDirectionResult>(results);

        public string ModelId => "test-engine";

        public string Description => "test";

        public PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles) =>
            _q.Dequeue();
    }

    private static IReadOnlyList<KiteHistoricalCandlePointDto> Candles(int count)
    {
        var t0 = DateTimeOffset.Parse("2026-05-08T09:15:00Z", System.Globalization.CultureInfo.InvariantCulture);
        return Enumerable.Range(0, count)
            .Select(i => new KiteHistoricalCandlePointDto(t0.AddMinutes(i), 100, 101, 99, 100 + i * 0.01m, 1_000))
            .ToList();
    }

    [Fact]
    public void TryCompute_up_up_down_majority_up()
    {
        var engine = new SequenceEngine(
            new PriceDirectionResult(PriceDirectionLabel.Up, 60, "t", "a"),
            new PriceDirectionResult(PriceDirectionLabel.Up, 55, "t", "b"),
            new PriceDirectionResult(PriceDirectionLabel.Down, 70, "t", "c"));
        var candles = Candles(52);

        Assert.True(PriceDirectionBestOfThree.TryCompute(candles, engine, minCandlesRequired: 50, out var merged, out var prefix));

        Assert.Equal(PriceDirectionLabel.Up, merged.Direction);
        Assert.StartsWith("[b3 u=2 d=1 n=0 v=up|up|down m=up]", prefix, StringComparison.Ordinal);
        Assert.Contains("a", merged.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseDetail_counts_and_roundtrip_detail()
    {
        const string detail = "[b3 u=2 d=1 n=0 v=up|up|down m=up] model says …";
        Assert.True(PriceDirectionBestOfThree.TryParseDetailCounts(detail, out var u, out var d, out var n));
        Assert.Equal(2, u);
        Assert.Equal(1, d);
        Assert.Equal(0, n);

        Assert.True(
            PriceDirectionBestOfThree.TryParseDetailExtended(detail, out _, out _, out _, out var votes, out var maj));
        Assert.Equal("up,up,down", votes);
        Assert.Equal("up", maj);
    }

    [Fact]
    public void SumVoteComponents_sums_across_rows()
    {
        var rows = new[]
        {
            "[b3 u=2 d=1 n=0 v=up|up|down m=up] x",
            "[b3 u=0 d=3 n=0 v=down|down|down m=down] y",
        };
        var (up, down, neu) = PriceDirectionBestOfThree.SumVoteComponents(rows);
        Assert.Equal(2, up);
        Assert.Equal(4, down);
        Assert.Equal(0, neu);
    }

    [Fact]
    public void TryCompute_insufficient_candles_returns_false()
    {
        var engine = new SequenceEngine(
            new PriceDirectionResult(PriceDirectionLabel.Up, 1, "t", ""),
            new PriceDirectionResult(PriceDirectionLabel.Up, 1, "t", ""),
            new PriceDirectionResult(PriceDirectionLabel.Up, 1, "t", ""));
        var candles = Candles(50);

        Assert.False(PriceDirectionBestOfThree.TryCompute(candles, engine, minCandlesRequired: 50, out _, out _));
    }
}
