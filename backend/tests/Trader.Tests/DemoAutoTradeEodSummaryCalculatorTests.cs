using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class DemoAutoTradeEodSummaryCalculatorTests
{
    [Fact]
    public void Charges_flat_and_turnover_bps_reduce_net_on_allocated_leg()
    {
        var row = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000071"),
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
            null,
            101m,
            "mlnet-sdca-logistic-v1");

        var fees = new DemoAutoTradeChargeParameters(true, 40m, 2m);
        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { row },
            10_000m,
            DemoAutoTradeStrategyIds.EqualSplit,
            fees);

        Assert.Equal(100m, totals.HypotheticalGrossPnlInr);
        Assert.Equal(42m, totals.HypotheticalChargesInr); // 40 + 10000 * 2 / 10000
        Assert.Equal(58m, totals.HypotheticalTotalPnlInr);
    }

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
            null,
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

    // When NextOpen is set, return is next open→next close (not ref→next close).
    [Fact]
    public void Market_style_uses_next_open_to_next_close_when_next_open_present()
    {
        var row = new MlAutomationPredictionListItemDto(
            Guid.Parse("00000000-0000-4000-8000-000000000081"),
            DateTimeOffset.Parse("2026-05-11T10:00:00Z"),
            "256265",
            "NIFTY",
            "NFO",
            "5m",
            DateTimeOffset.Parse("2026-05-11T09:30:00Z"),
            999m,
            "up",
            72,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            100m,
            101m,
            "e");

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { row },
            10_000m,
            DemoAutoTradeStrategyIds.EqualSplit);

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
            null,
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
            null,
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
            null,
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
            null,
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
            null,
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
            null,
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
    public void One_signal_per_engine_keeps_highest_confidence_per_engine_id()
    {
        var low = Row(
            Guid.Parse("00000000-0000-4000-8000-000000000031"),
            "100",
            "up",
            55,
            "eng-a");
        var high = Row(
            Guid.Parse("00000000-0000-4000-8000-000000000032"),
            "200",
            "up",
            90,
            "eng-a");
        var otherEngine = Row(
            Guid.Parse("00000000-0000-4000-8000-000000000033"),
            "300",
            "up",
            40,
            "eng-b");

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { low, high, otherEngine },
            9_000m,
            DemoAutoTradeStrategyIds.OneSignalPerEngine);

        Assert.Equal(3, totals.DirectionalTradeableLegs);
        Assert.Equal(1, totals.SkippedLowConfidenceLegs); // one duplicate eng-a leg dropped
        Assert.Equal(2, totals.AllocatedLegsForPnl);
        // high: +1% on 4500; otherEngine: +1% on 4500 → 90
        Assert.Equal(90m, totals.HypotheticalTotalPnlInr);
    }

    [Fact]
    public void Top_half_confidence_keeps_upper_half_then_equal_split()
    {
        var rows = new[]
        {
            Mk(Guid.Parse("00000000-0000-4000-8000-000000000041"), 30),
            Mk(Guid.Parse("00000000-0000-4000-8000-000000000042"), 50),
            Mk(Guid.Parse("00000000-0000-4000-8000-000000000043"), 70),
        };

        var totals = DemoAutoTradeEodSummaryCalculator.Compute(rows, 10_000m, DemoAutoTradeStrategyIds.TopHalfConfidence);

        Assert.Equal(3, totals.DirectionalTradeableLegs);
        Assert.Equal(1, totals.SkippedLowConfidenceLegs);
        Assert.Equal(2, totals.AllocatedLegsForPnl);
        // top 2: 70 and 50, 5k each, both +1% → 100
        Assert.Equal(100m, totals.HypotheticalTotalPnlInr);
    }

    [Fact]
    public void Signal_strength_squared_concentrates_notional_on_high_confidence()
    {
        var low = SqRow(Guid.Parse("00000000-0000-4000-8000-000000000051"), 50);
        var high = SqRow(Guid.Parse("00000000-0000-4000-8000-000000000052"), 100);
        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { low, high },
            10_000m,
            DemoAutoTradeStrategyIds.SignalStrengthSquared);
        // weights 50² : 100² = 1:4 → 2k / 8k notionals, both +1% → 100
        Assert.Equal(100m, totals.HypotheticalTotalPnlInr);
    }

    [Fact]
    public void Implied_edge_weighted_zero_when_all_confidence_at_or_below_fifty()
    {
        var a = EdgeRow(Guid.Parse("00000000-0000-4000-8000-000000000061"), 50);
        var b = EdgeRow(Guid.Parse("00000000-0000-4000-8000-000000000062"), 40);
        var totals = DemoAutoTradeEodSummaryCalculator.Compute(
            new[] { a, b },
            10_000m,
            DemoAutoTradeStrategyIds.ImpliedEdgeWeighted);
        Assert.Equal(0, totals.AllocatedLegsForPnl);
        Assert.Equal(0m, totals.HypotheticalTotalPnlInr);
        Assert.Equal(2, totals.SkippedLowConfidenceLegs);
    }

    [Fact]
    public void DemoAutoTradeStrategyIds_ParseRequired_rejects_unknown_slug()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DemoAutoTradeStrategyIds.ParseRequired("not-a-real-strategy"));
        Assert.Contains("Unknown demo strategy", ex.Message);
    }

    private static MlAutomationPredictionListItemDto Row(Guid id, string token, string dir, int conf, string engine) =>
        new(
            id,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            token,
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            dir,
            conf,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            null,
            101m,
            engine);

    private static MlAutomationPredictionListItemDto Mk(Guid id, int conf) =>
        new(
            id,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            "1",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            "up",
            conf,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            null,
            101m,
            "e");

    private static MlAutomationPredictionListItemDto SqRow(Guid id, int conf) =>
        new(
            id,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            "1",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            "up",
            conf,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            null,
            101m,
            "e");

    private static MlAutomationPredictionListItemDto EdgeRow(Guid id, int conf) =>
        new(
            id,
            DateTimeOffset.Parse("2026-05-11T12:00:00Z"),
            "1",
            "S",
            "NSE",
            "5m",
            DateTimeOffset.Parse("2026-05-11T08:00:00Z"),
            100m,
            "up",
            conf,
            "correct",
            DateTimeOffset.Parse("2026-05-11T09:35:00Z"),
            null,
            101m,
            "e");

    [Fact]
    public void ComputeWithLegRows_totals_match_Compute_and_allocated_leg_present()
    {
        var rows = new[]
        {
            new MlAutomationPredictionListItemDto(
                Guid.Parse("00000000-0000-4000-8000-000000000041"),
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
                null,
                101m,
                "mlnet-sdca-logistic-v1"),
        };
        var fees = new DemoAutoTradeChargeParameters(true, 40m, 2m);
        var a = DemoAutoTradeEodSummaryCalculator.Compute(rows, 10_000m, DemoAutoTradeStrategyIds.EqualSplit, fees);
        var (b, legs) = DemoAutoTradeEodSummaryCalculator.ComputeWithLegRows(
            rows,
            10_000m,
            DemoAutoTradeStrategyIds.EqualSplit,
            fees);
        Assert.Equal(a, b);
        Assert.Single(legs);
        Assert.Equal("allocated", legs[0].Status);
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

    [Fact]
    public void GetLocalDateOnly_kolkata_maps_utc_noon_to_same_calendar_day()
    {
        var d = DemoAutoTradeEodSummaryCalculator.GetLocalDateOnly(
            DateTimeOffset.Parse("2026-05-11T06:30:00Z"),
            "Asia/Kolkata");
        Assert.Equal(new DateOnly(2026, 5, 11), d);
    }
}
