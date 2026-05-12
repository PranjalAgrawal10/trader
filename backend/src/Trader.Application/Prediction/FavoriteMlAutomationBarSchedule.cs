using Trader.Application.Broker;
using Trader.Application.Configuration;

namespace Trader.Application.Prediction;

/// <summary>
/// Optional intraday guards for favorite ML automation:
/// exchanges-style local session windows (India cash/F&amp;O) and coarse IST clock alignment (“round-minute” buckets).
/// </summary>
public static class FavoriteMlAutomationBarSchedule
{
    /// <summary>When MCX commodities have evening sessions, skip equity-style session gates so automation can still run.</summary>
    public static bool LooksLikeMcxExchange(string? exchange) =>
        string.Equals(exchange?.Trim(), "MCX", StringComparison.OrdinalIgnoreCase);

    /// <summary>When false, commodity favorites still respect minute-phase alignment unless disabled separately.</summary>
    public static bool ShouldApplyEquityLikeTradingSession(
        FavoriteMlAutomationOptions opts,
        string? favoriteExchangeTrimmed)
    {
        if (!opts.TradingSessionRestrictionsEnabled)
            return false;

        if (opts.SkipTradingSessionForMcxFavorites && LooksLikeMcxExchange(favoriteExchangeTrimmed))
            return false;

        return true;
    }

    /// <summary>Daily-or-longer candles are not limited to equity intraday hours.</summary>
    public static bool IsIntradayOrShorterHistogram(string normalizedInterval)
    {
        var barLen = ChartUiIntervals.BarDuration(normalizedInterval);
        return barLen < TimeSpan.FromDays(1);
    }

    /// <returns>True when a <strong>new</strong> automation prediction should be deferred (skipped this tick).</returns>
    public static bool ShouldDeferOutsideTradingSession(
        FavoriteMlAutomationOptions opts,
        DateTime utcNow,
        string normalizedInterval,
        string? favoriteExchangeTrimmed)
    {
        if (!ShouldApplyEquityLikeTradingSession(opts, favoriteExchangeTrimmed))
            return false;

        if (!IsIntradayOrShorterHistogram(normalizedInterval))
            return false;

        var tz = ResolveReportTimeZone(opts.ReportTimeZoneId);
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        var t = TimeOnly.FromDateTime(local);
        var start = FavoriteMlAutomationQuietHours.ToTimeOnly(
            opts.TradingSessionStartLocalHour,
            opts.TradingSessionStartLocalMinute);
        var end = FavoriteMlAutomationQuietHours.ToTimeOnly(
            opts.TradingSessionEndLocalHour,
            opts.TradingSessionEndLocalMinute);
        if (start == end)
            return false;

        return !(t >= start && t < end);
    }

    /// <summary>
    /// Restricts firing to the first <see cref="FavoriteMlAutomationOptions.IstMinuteBoundarySeconds"/> seconds of each
    /// wall-clock minute in <see cref="FavoriteMlAutomationOptions.ReportTimeZoneId"/> so polls line up with “round minute” timestamps.
    /// Skipped when disabled, for multi-day candles, or for <strong>N</strong>-minute cadence (handled by caller).
    /// </summary>
    public static bool ShouldDeferOutsideIstMinutePhase(
        FavoriteMlAutomationOptions opts,
        DateTimeOffset utcNow,
        string normalizedInterval)
    {
        if (!opts.IstMinuteBoundaryAlignmentEnabled)
            return false;

        var code = normalizedInterval.Trim().ToLowerInvariant();
        if (code is "1d" or "1w")
            return false;

        var sec = Math.Clamp(opts.IstMinuteBoundarySeconds, 5, 45);
        var tz = ResolveReportTimeZone(opts.ReportTimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow.UtcDateTime, tz);
        // Align with “round” clock buckets: observe only within the first N seconds of each local minute (covers poll jitter).
        return local.Second >= sec;
    }

    public static TimeZoneInfo ResolveReportTimeZone(string? id)
    {
        var tid = string.IsNullOrWhiteSpace(id) ? "Asia/Kolkata" : id.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tid);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return tid.Equals("Asia/Kolkata", StringComparison.OrdinalIgnoreCase)
                    ? TimeZoneInfo.FindSystemTimeZoneById("India Standard Time")
                    : TimeZoneInfo.Utc;
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
