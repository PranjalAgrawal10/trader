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

    /// <summary>
    /// When &gt; 0, delay this many seconds between automation cycles (clamped 15–3600).
    /// When 0 (default), <see cref="PollIntervalMinutes"/> is used instead.
    /// </summary>
    public int PollIntervalSeconds { get; set; }

    /// <summary>How often to resolve + predict when <see cref="PollIntervalSeconds"/> is 0 (minutes, 1–120).</summary>
    public int PollIntervalMinutes { get; set; } = 1;

    /// <summary>
    /// Candle interval for favorite automation predictions only (e.g. <c>1m</c>).
    /// When null or whitespace, uses saved chart interval (global toolbar + per-favorite overrides).
    /// Use <c>1m</c> here to run on one-minute bars so automation is not blocked until a 3m/5m bar completes.
    /// </summary>
    public string? PredictionIntervalOverride { get; set; } = "1m";

    /// <summary>IANA or Windows TZ id (e.g. <c>Asia/Kolkata</c>, <c>India Standard Time</c>).</summary>
    public string ReportTimeZoneId { get; set; } = "Asia/Kolkata";

    /// <summary>Local hour (0–23) to start the daily CSV + pie send window (same timezone as <see cref="ReportTimeZoneId"/>; default 23 = 11 PM).</summary>
    public int ReportLocalHour { get; set; } = 23;

    /// <summary>Local minute (0–59); default 30 → 11:30 PM when <see cref="ReportTimeZoneId"/> is IST.</summary>
    public int ReportLocalMinute { get; set; } = 30;

    /// <summary>Fallback chart interval when user has never saved instruments chart settings.</summary>
    public string DefaultChartInterval { get; set; } = "5m";

    /// <summary>Max pending rows to scan for resolution per user per tick.</summary>
    public int MaxPendingResolutionBatch { get; set; } = 2_000;
}
