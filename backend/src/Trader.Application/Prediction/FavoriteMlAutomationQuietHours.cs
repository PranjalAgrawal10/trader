using Trader.Application.Configuration;

namespace Trader.Application.Prediction;

/// <summary>
/// Daily window when scheduled favorite ML automation skips new predictions + resolution (EOD mail still evaluated separately).
/// Times are in <see cref="FavoriteMlAutomationOptions.ReportTimeZoneId"/> (default <c>Asia/Kolkata</c> = IST).
/// </summary>
public static class FavoriteMlAutomationQuietHours
{
    /// <summary>
    /// When <see cref="FavoriteMlAutomationOptions.QuietHoursEnabled"/> is false, always false.
    /// Pause interval is [<see cref="FavoriteMlAutomationOptions.QuietHoursStartLocalHour"/>:<see cref="FavoriteMlAutomationOptions.QuietHoursStartLocalMinute"/>,
    /// <see cref="FavoriteMlAutomationOptions.QuietHoursEndLocalHour"/>:<see cref="FavoriteMlAutomationOptions.QuietHoursEndLocalMinute"/>);
    /// end is exclusive (automation resumes on that clock time).
    /// If start and end coincide after clamping, no pause applies.
    /// </summary>
    public static bool IsAutomationPaused(FavoriteMlAutomationOptions opts, DateTime utcNow)
    {
        if (!opts.QuietHoursEnabled)
            return false;

        var start = ToTimeOnly(opts.QuietHoursStartLocalHour, opts.QuietHoursStartLocalMinute);
        var end = ToTimeOnly(opts.QuietHoursEndLocalHour, opts.QuietHoursEndLocalMinute);
        if (start == end)
            return false;

        var tz = ResolveTimeZone(opts.ReportTimeZoneId);
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        var t = TimeOnly.FromDateTime(local);

        if (start < end)
            return t >= start && t < end;

        return t >= start || t < end;
    }

    internal static TimeOnly ToTimeOnly(int hour, int minute)
    {
        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return new TimeOnly(hour, minute);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        var tid = string.IsNullOrWhiteSpace(id) ? "Asia/Kolkata" : id.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tid);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
