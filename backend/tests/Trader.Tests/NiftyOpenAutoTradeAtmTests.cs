using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Tests;

public sealed class NiftyOpenAutoTradeAtmTests
{
    [Fact]
    public void ComputeAffordableLots_ScalesWithBalance()
    {
        // premium 100, lot 75 → 7500 per lot
        Assert.Equal(0, NiftyOpenAutoTradeAtm.ComputeAffordableLots(7000m, 100m, 75, 5, 1m));
        Assert.Equal(1, NiftyOpenAutoTradeAtm.ComputeAffordableLots(8000m, 100m, 75, 5, 1m));
        Assert.Equal(5, NiftyOpenAutoTradeAtm.ComputeAffordableLots(100_000m, 100m, 75, 5, 1m));
        Assert.Equal(3, NiftyOpenAutoTradeAtm.ComputeAffordableLots(30_000m, 100m, 75, 5, 0.95m));
    }

    [Fact]
    public void BuildStrikeCandidates_PrefersAtmThenDistance()
    {
        var expiry = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero).ToString("O");
        var rows = new List<KiteInstrumentListItemDto>
        {
            Row("1", "NIFTY25JUL24500CE", 24500m, expiry, "CE"),
            Row("2", "NIFTY25JUL24600CE", 24600m, expiry, "CE"),
            Row("3", "NIFTY25JUL24700CE", 24700m, expiry, "CE"),
            Row("4", "NIFTY25JUL24600PE", 24600m, expiry, "PE"),
        };

        var ce = NiftyOpenAutoTradeAtm.BuildStrikeCandidates(
            rows,
            DateTimeOffset.Parse(expiry),
            spotLtp: 24610m,
            optionSide: "CE",
            maxStepsAwayFromAtm: 2);

        Assert.Equal(3, ce.Count);
        Assert.Equal(24600m, ce[0].Strike);
        Assert.All(ce, c => Assert.Equal("CE", c.InstrumentType));
    }

    [Fact]
    public void FilterNiftyOptions_ExcludesBankNifty()
    {
        var expiry = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero).ToString("O");
        var rows = new List<KiteInstrumentListItemDto>
        {
            Row("1", "NIFTY25JUL24600CE", 24600m, expiry, "CE"),
            Row("2", "BANKNIFTY25JUL52000CE", 52000m, expiry, "CE", name: "BANKNIFTY"),
        };

        var filtered = NiftyOpenAutoTradeAtm.FilterNiftyOptions(rows);
        Assert.Single(filtered);
        Assert.Equal("NIFTY25JUL24600CE", filtered[0].Tradingsymbol);
    }

    [Fact]
    public void FireWindow_IsInclusiveAtOpen()
    {
        var opts = new NiftyOpenAutoTradeOptions
        {
            FireLocalHour = 9,
            FireLocalMinute = 15,
            FireWindowSeconds = 120,
            PauseOnWeekends = true,
            TimeZoneId = "India Standard Time",
        };

        // Wednesday 2026-07-15 09:15:00 IST = 03:45 UTC
        var atOpen = new DateTimeOffset(2026, 7, 15, 3, 45, 0, TimeSpan.Zero);
        Assert.True(NiftyOpenAutoTradeSchedule.IsInsideFireWindow(opts, atOpen));

        var before = atOpen.AddMinutes(-1);
        Assert.False(NiftyOpenAutoTradeSchedule.IsInsideFireWindow(opts, before));

        var afterWindow = atOpen.AddSeconds(121);
        Assert.False(NiftyOpenAutoTradeSchedule.IsInsideFireWindow(opts, afterWindow));
    }

    [Fact]
    public void Trail_InitialStop_IsEntryMinusPoints()
    {
        Assert.Equal(95m, NiftyOpenAutoTradeTrail.InitialStopPrice(100m, 5m, 0.05m));
        Assert.Equal(0.05m, NiftyOpenAutoTradeTrail.InitialStopPrice(3m, 5m, 0.05m));
    }

    [Fact]
    public void Trail_RaisesStop_WhenPeakAdvances()
    {
        var (peak1, stop1) = NiftyOpenAutoTradeTrail.ComputeTrailUpdate(
            peakPrice: 100m,
            currentStop: 95m,
            ltp: 100m,
            trailPoints: 5m,
            tickSize: 0.05m);
        Assert.Equal(100m, peak1);
        Assert.Null(stop1);

        var (peak2, stop2) = NiftyOpenAutoTradeTrail.ComputeTrailUpdate(
            peakPrice: 100m,
            currentStop: 95m,
            ltp: 108m,
            trailPoints: 5m,
            tickSize: 0.05m);
        Assert.Equal(108m, peak2);
        Assert.Equal(103m, stop2);
    }

    [Fact]
    public void TrailWindow_SpansOpenThroughTrailEnd()
    {
        var opts = new NiftyOpenAutoTradeOptions
        {
            FireLocalHour = 9,
            FireLocalMinute = 15,
            TrailEndLocalHour = 15,
            TrailEndLocalMinute = 25,
            PauseOnWeekends = true,
            TimeZoneId = "India Standard Time",
        };

        // Wednesday 2026-07-15 10:00 IST = 04:30 UTC
        var midMorning = new DateTimeOffset(2026, 7, 15, 4, 30, 0, TimeSpan.Zero);
        Assert.True(NiftyOpenAutoTradeSchedule.IsInsideTrailWindow(opts, midMorning));

        // 15:25 IST = 09:55 UTC — exclusive end
        var atEnd = new DateTimeOffset(2026, 7, 15, 9, 55, 0, TimeSpan.Zero);
        Assert.False(NiftyOpenAutoTradeSchedule.IsInsideTrailWindow(opts, atEnd));
    }

    [Fact]
    public void ResolveExpiry_PrefersUserSelectedDate()
    {
        var expiryA = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);
        var expiryB = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
        var rows = new List<KiteInstrumentListItemDto>
        {
            Row("1", "NIFTY25JUL24600CE", 24600m, expiryA.ToString("O"), "CE"),
            Row("2", "NIFTY25JUL24600CE", 24600m, expiryB.ToString("O"), "CE"),
        };

        var resolved = NiftyOpenAutoTradeAtm.ResolveExpiryUtc(
            rows,
            preferredExpiry: DateOnly.FromDateTime(expiryB.UtcDateTime),
            utcNow: new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(expiryB.UtcDateTime.Date, resolved!.Value.UtcDateTime.Date);
        Assert.Equal(
            new[] { "2026-07-16", "2026-07-23" },
            NiftyOpenAutoTradeAtm.ListDistinctExpiryDates(rows));
    }

    private static KiteInstrumentListItemDto Row(
        string token,
        string symbol,
        decimal strike,
        string expiry,
        string type,
        string? name = null) =>
        new(
            token,
            symbol,
            "NFO",
            name ?? "NIFTY",
            type,
            "NFO-OPT",
            expiry,
            strike,
            75);
}
