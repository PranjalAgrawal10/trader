using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class FavoriteMlAutomationCandleValidatorTests
{
    private static KiteHistoricalCandlePointDto Bar(DateTimeOffset t, decimal o, decimal h, decimal l, decimal c, long v = 1) =>
        new(t, o, h, l, c, v);

    [Fact]
    public void Too_few_bars_rejected()
    {
        var candles = new List<KiteHistoricalCandlePointDto>();
        for (var i = 0; i < 10; i++)
            candles.Add(Bar(new DateTimeOffset(2026, 5, 11, 9, i, 0, TimeSpan.Zero), 1, 2, 0.5m, 1.5m));

        Assert.False(FavoriteMlAutomationCandleValidator.IsValidForPrediction(candles, out var r));
        Assert.Contains("48", r ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void High_below_low_rejected()
    {
        var t0 = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50)
            .Select(i => Bar(t0.AddMinutes(i), 1, 2, 0.5m, 1.5m))
            .ToList();
        candles[^1] = Bar(t0.AddMinutes(49), 1, 0.5m, 2m, 1m);

        Assert.False(FavoriteMlAutomationCandleValidator.IsValidForPrediction(candles, out var r));
        Assert.Contains("high < low", r ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Non_monotonic_time_rejected()
    {
        var t0 = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50)
            .Select(i => Bar(t0.AddMinutes(i), 1, 2, 0.5m, 1.5m))
            .ToList();
        candles[25] = Bar(t0.AddMinutes(10), 1, 2, 0.5m, 1.5m);

        Assert.False(FavoriteMlAutomationCandleValidator.IsValidForPrediction(candles, out var r));
        Assert.Contains("not strictly increasing", r ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_series_accepted()
    {
        var t0 = new DateTimeOffset(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);
        var candles = Enumerable.Range(0, 50)
            .Select(i => Bar(t0.AddMinutes(i), 1, 2, 0.5m, 1.5m))
            .ToList();

        Assert.True(FavoriteMlAutomationCandleValidator.IsValidForPrediction(candles, out var r));
        Assert.Null(r);
    }
}
