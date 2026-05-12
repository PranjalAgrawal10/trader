using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class FavoriteMlAutomationBarScheduleTests
{
    private static FavoriteMlAutomationOptions RestrictedDefaults() =>
        new()
        {
            ReportTimeZoneId = "Asia/Kolkata",
            TradingSessionRestrictionsEnabled = true,
            TradingSessionStartLocalHour = 9,
            TradingSessionStartLocalMinute = 15,
            TradingSessionEndLocalHour = 15,
            TradingSessionEndLocalMinute = 30,
            SkipTradingSessionForMcxFavorites = true,
            IstMinuteBoundaryAlignmentEnabled = true,
            IstMinuteBoundarySeconds = 22,
        };

    [Fact]
    public void Session_before_open_defers_equity_not_mcx()
    {
        var o = RestrictedDefaults();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 7, 44, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);

        Assert.True(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "5m", "NFO"));

        Assert.False(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "5m", "MCX"));
    }

    [Fact]
    public void Session_before_open_defers_when_exchange_unknown()
    {
        var o = RestrictedDefaults();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 7, 44, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        Assert.True(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "5m", null));
    }
    [Fact]
    public void Session_inside_window_not_deferred()
    {
        var o = RestrictedDefaults();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 11, 30, 10, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        Assert.False(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "1m", "NFO"));
    }

    [Fact]
    public void Session_at_end_exclusive_deferred()
    {
        var o = RestrictedDefaults();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 15, 30, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        Assert.True(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "1m", "NSE"));
    }
    [Fact]
    public void Session_disabled_never_defers()
    {
        var o = RestrictedDefaults();
        o.TradingSessionRestrictionsEnabled = false;
        var utc = new DateTime(2026, 6, 8, 2, 14, 55, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "5m", "NFO"));
    }

    [Fact]
    public void Daily_interval_exempt_from_session()
    {
        var o = RestrictedDefaults();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 7, 44, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        Assert.False(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideTradingSession(o, utc, "1w", "NFO"));
    }

    [Fact]
    public void Ist_minute_phase_defers_after_slack()
    {
        var o = RestrictedDefaults();
        o.IstMinuteBoundarySeconds = 10;
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 12, 5, 15, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        Assert.True(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideIstMinutePhase(o, new DateTimeOffset(utc, TimeSpan.Zero), "5m"));

        var localEarly = new DateTime(2026, 6, 9, 12, 5, 5, 0, DateTimeKind.Unspecified);
        var utcEarly = TimeZoneInfo.ConvertTimeToUtc(localEarly, tz);
        Assert.False(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideIstMinutePhase(o, new DateTimeOffset(utcEarly, TimeSpan.Zero), "5m"));
    }

    [Fact]
    public void Ist_minute_phase_skipped_for_daily()
    {
        var o = RestrictedDefaults();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var local = new DateTime(2026, 6, 9, 12, 5, 45, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        Assert.False(FavoriteMlAutomationBarSchedule.ShouldDeferOutsideIstMinutePhase(o, new DateTimeOffset(utc, TimeSpan.Zero), "1d"));
    }
}
