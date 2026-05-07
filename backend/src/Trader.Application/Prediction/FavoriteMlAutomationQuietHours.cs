using Trader.Application.Configuration;

namespace Trader.Application.Prediction;

/// <summary>
/// Daily window when the favorite-ML background job skips only <strong>new</strong> scheduled predictions per favorite/engine.
/// Pending-row resolution still runs so outcomes can settle; nightly EOD email is unchanged.
/// Uses <see cref="FavoriteMlAutomationOptions.ReportTimeZoneId"/> (default <c>Asia/Kolkata</c> = IST).
/// </summary>
public static class FavoriteMlAutomationQuietHours
{
    /// <summary>
    /// When <see cref="FavoriteMlAutomationOptions.QuietHoursEnabled"/> is false, always false.
    /// When local time (<see cref="FavoriteMlAutomationOptions.ReportTimeZoneId"/>) falls in
    /// [<see cref="FavoriteMlAutomationOptions.QuietHoursStartLocalHour"/>:<see cref="FavoriteMlAutomationOptions.QuietHoursStartLocalMinute"/>,
    /// <see cref="FavoriteMlAutomationOptions.QuietHoursEndLocalHour"/>:<see cref="FavoriteMlAutomationOptions.QuietHoursEndLocalMinute"/>),
    /// returns <c>true</c> so the automation loop skips only <strong>new</strong> scheduled predictions—pending resolutions and EOD email still run.
    /// End time is exclusive. If start and end coincide after clamping, returns false.
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
