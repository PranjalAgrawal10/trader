using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class FavoriteMlAutomationIntrabarTests
{
    private static readonly DateTimeOffset Open = new(2026, 5, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Zero_delay_always_ready()
    {
        Assert.True(
            FavoriteMlAutomationIntrabar.IsReadyForNewPredictionOnRefBar(
                Open,
                Open,
                ChartUiIntervals.BarDuration("5m"),
                0));
        Assert.True(
            FavoriteMlAutomationIntrabar.IsReadyForNewPredictionOnRefBar(
                Open.AddSeconds(1),
                Open,
                ChartUiIntervals.BarDuration("5m"),
                0));
    }

    [Fact]
    public void Before_threshold_not_ready()
    {
        Assert.False(
            FavoriteMlAutomationIntrabar.IsReadyForNewPredictionOnRefBar(
                Open.AddSeconds(9),
                Open,
                ChartUiIntervals.BarDuration("5m"),
                10));
    }

    [Fact]
    public void At_threshold_ready_intrabar()
    {
        Assert.True(
            FavoriteMlAutomationIntrabar.IsReadyForNewPredictionOnRefBar(
                Open.AddSeconds(10),
                Open,
                ChartUiIntervals.BarDuration("5m"),
                10));
    }

    [Fact]
    public void Huge_requested_delay_clamps_below_bar_length()
    {
        // 1m bar: max delay 59s; request 999 → still ready at open+59
        Assert.False(
            FavoriteMlAutomationIntrabar.IsReadyForNewPredictionOnRefBar(
                Open.AddSeconds(58),
                Open,
                ChartUiIntervals.BarDuration("1m"),
                999));
        Assert.True(
            FavoriteMlAutomationIntrabar.IsReadyForNewPredictionOnRefBar(
                Open.AddSeconds(59),
                Open,
                ChartUiIntervals.BarDuration("1m"),
                999));
    }
}
