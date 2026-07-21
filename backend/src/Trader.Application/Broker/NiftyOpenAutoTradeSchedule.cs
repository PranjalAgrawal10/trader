using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Application.Broker;

/// <summary>IST session clock helpers for the 09:15 NIFTY open auto-trade fire window.</summary>
public static class NiftyOpenAutoTradeSchedule
{
    public static TimeZoneInfo ResolveTimeZone(string? id) =>
        FavoriteMlAutomationBarSchedule.ResolveReportTimeZone(
            string.IsNullOrWhiteSpace(id) ? "Asia/Kolkata" : id);

    public static DateOnly GetSessionDateIst(DateTimeOffset utcNow, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, tz);
        return DateOnly.FromDateTime(local);
    }

    public static bool IsWeekend(DateOnly sessionDate)
    {
        var dow = sessionDate.DayOfWeek;
        return dow is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    /// <summary>True when local clock is within [fire, fire+window) on a weekday (optional weekend pause).</summary>
    public static bool IsInsideFireWindow(NiftyOpenAutoTradeOptions opts, DateTimeOffset utcNow)
    {
        var tz = ResolveTimeZone(opts.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, tz);
        var sessionDate = DateOnly.FromDateTime(local);

        if (opts.PauseOnWeekends && IsWeekend(sessionDate))
            return false;

        var start = new TimeOnly(
            Math.Clamp(opts.FireLocalHour, 0, 23),
            Math.Clamp(opts.FireLocalMinute, 0, 59));
        var window = TimeSpan.FromSeconds(Math.Clamp(opts.FireWindowSeconds, 15, 600));
        var t = TimeOnly.FromDateTime(local);
        var end = start.Add(window);
        if (end <= start)
            return t >= start;

        return t >= start && t < end;
    }

    /// <summary>
    /// True from the fire clock through <see cref="NiftyOpenAutoTradeOptions.TrailEndLocalHour"/>:<see cref="NiftyOpenAutoTradeOptions.TrailEndLocalMinute"/>
    /// on weekdays (when weekend pause is on) — used to manage trailing GTT stops after entry.
    /// </summary>
    public static bool IsInsideTrailWindow(NiftyOpenAutoTradeOptions opts, DateTimeOffset utcNow)
    {
        var tz = ResolveTimeZone(opts.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, tz);
        var sessionDate = DateOnly.FromDateTime(local);

        if (opts.PauseOnWeekends && IsWeekend(sessionDate))
            return false;

        var start = new TimeOnly(
            Math.Clamp(opts.FireLocalHour, 0, 23),
            Math.Clamp(opts.FireLocalMinute, 0, 59));
        var end = new TimeOnly(
            Math.Clamp(opts.TrailEndLocalHour, 0, 23),
            Math.Clamp(opts.TrailEndLocalMinute, 0, 59));
        var t = TimeOnly.FromDateTime(local);
        if (end <= start)
            return t >= start;
        return t >= start && t < end;
    }
}
