using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class DemoAutoTradeEodSummaryCalculatorTests
{
    [Fact]
    public void EqualSplit_single_up_correct_one_percent_gain_full_notional_leg()
    {
        var row = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000001"),
            DateTimeOffset.Parse("2026-05-11T10:00:00Z"),
            "256265",
            "NIFTY",
            "NFO",
            "5m",
            DateTimeOffset.Parse("2026-05-11T09:30:00Z"),
            100m,
            "up",
            72,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            101m,
            "mlnet-sdca-logistic-v1");

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { row },
            10_000m,
            DemoAutoTradeStrategyIds.EqualSplit);

        Assert.Equal(1, totals.TotalSignals);
        Assert.Equal(0, totals.PendingSignals);
        Assert.Equal(1, totals.CorrectOutcomes);
        Assert.Equal(0, totals.WrongOutcomes);
        Assert.Equal(1, totals.DirectionalTradeableLegs);
        Assert.Equal(1, totals.AllocatedLegsForPnl);
        Assert.Equal(100m, totals.HypotheticalTotalPnlInr);
    }

    [Fact]
    public void EqualSplit_two_rows_split_notional_each_half()
    {
        var a = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000002"),
            DateTimeOffset.Parse("2026-05-11T11:00:00Z"),
            "1",
            "A",
            "NSE",
            "1m",
            DateTimeOffset.Parse("2026-05-11T10:00:00Z"),
            200m,
            "up",
            50,
            "correct",
            DateTimeOffset.Parse("2026-05-11T10:01:00Z"),
            200m,
            "e");

        var b = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000003"),
            DateTimeOffset.Parse("2026-05-11T11:01:00Z"),
            "2",
            "B",
            "NSE",
            "1m",
            DateTimeOffset.Parse("2026-05-11T10:02:00Z"),
            50m,
            "down",
            50,
            "wrong",
            DateTimeOffset.Parse("2026-05-11T10:03:00Z"),
            60m,
            "e");

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { a, b },
            10_000m,
            DemoAutoTradeStrategyIds.EqualSplit);

        Assert.Equal(2, totals.TotalSignals);
        Assert.Equal(2, totals.DirectionalTradeableLegs);
        Assert.Equal(2, totals.AllocatedLegsForPnl);
        // a: up, ref 200 next 200 -> 0
        // b: down, ref 50 next 60 -> short loses: -(60-50)/50 = -0.2 -> -0.2 * 5000 = -1000
        Assert.Equal(-1000m, totals.HypotheticalTotalPnlInr);
    }

    [Fact]
    public void HighConviction_filters_low_confidence_before_split()
    {
        var lowConf = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000011"),
            DateTimeOffset.Parse("2026-05-11T12:01:00Z"),
            "9",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            "up",
            40,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            110m,
            "e");

        var highConf = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000012"),
            DateTimeOffset.Parse("2026-05-11T12:02:00Z"),
            "9",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            "up",
            80,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            101m,
            "e");

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { lowConf, highConf },
            10_000m,
            DemoAutoTradeStrategyIds.HighConviction);

        Assert.Equal(2, totals.DirectionalTradeableLegs);
        Assert.Equal(1, totals.SkippedLowConfidenceLegs);
        Assert.Equal(1, totals.AllocatedLegsForPnl);
        Assert.Equal(100m, totals.HypotheticalTotalPnlInr); // full 10k on the +1% leg
    }

    [Fact]
    public void One_signal_per_instrument_keeps_highest_confidence_row()
    {
        var weak = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000021"),
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            "123",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            "up",
            50,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            101m,
            "e");

        var strong = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000022"),
            DateTimeOffset.Parse("2026-05-11T12:05:00Z"),
            "123",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            50m,
            "up",
            90,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            50m,
            "e");

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { weak, strong },
            10_000m,
            DemoAutoTradeStrategyIds.OneSignalPerInstrument);

        Assert.Equal(2, totals.DirectionalTradeableLegs);
        Assert.Equal(1, totals.AllocatedLegsForPnl);
        Assert.Equal(0m, totals.HypotheticalTotalPnlInr);
    }

    [Fact]
    public void GetLocalDayBoundsUtc_kolkata_midnight_spans_expected_utc_window()
    {
        var (from, to) = DemoAutoTradeEodSummaryCalculator.GetLocalDayBoundsUtc(
            new DateOnly(2026, 5, 11),
            "Asia/Kolkata");

        Assert.True(from < to);
        Assert.Equal(TimeSpan.FromHours(24), to - from);
    }
}
