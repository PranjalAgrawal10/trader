namespace Trader.Application.Configuration;

/// <summary>
/// Background tick: resolve pending favorite ML predictions, run new predictions per favorite, optional EOD email.
/// Disabled by default; requires a live Kite session per user and SMTP for reports.
/// </summary>
public sealed class FavoriteMlAutomationOptions
{
    public const string SectionName = "FavoriteMlAutomation";

    /// <summary>When false, the hosted service idles with a longer sleep.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often to resolve + predict for users with favorites (minutes).</summary>
    public int PollIntervalMinutes { get; set; } = 1;

    /// <summary>IANA or Windows TZ id (e.g. <c>Asia/Kolkata</c>, <c>India Standard Time</c>).</summary>
    public string ReportTimeZoneId { get; set; } = "Asia/Kolkata";

    /// <summary>Local time of day to send the daily CSV + pie chart (same timezone as <see cref="ReportTimeZoneId"/>).</summary>
    public int ReportLocalHour { get; set; } = 20;

    public int ReportLocalMinute { get; set; }

    /// <summary>Fallback chart interval when user has never saved instruments chart settings.</summary>
    public string DefaultChartInterval { get; set; } = "5m";

    /// <summary>Max pending rows to scan for resolution per user per tick.</summary>
    public int MaxPendingResolutionBatch { get; set; } = 2_000;
}
