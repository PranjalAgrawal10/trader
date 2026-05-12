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
    /// Candle interval for favorite automation predictions only (e.g. <c>1m</c>) when the user has not set{' '}
    /// <see cref="Domain.Entities.User.FavoriteMlAutomationInterval"/>.
    /// When null or whitespace, uses saved chart interval (global toolbar + per-favorite overrides).
    /// Use <c>1m</c> here to run on one-minute bars so automation is not blocked until a 3m/5m bar completes.
    /// </summary>
    public string? PredictionIntervalOverride { get; set; } = "1m";

    /// <summary>
    /// When &gt; 0, a <strong>new</strong> automation prediction for the current reference bar (last candle from Kite,
    /// usually still forming) is deferred until at least this many seconds have passed since that bar&apos;s open time
    /// (Kite candle <c>Time</c> = bar open). This does <strong>not</strong> wait for the full bar
    /// interval to finish — once the threshold is met, the next automation pass may persist a row (still at most one
    /// pending row per ref bar per engine). Values larger than one bar length minus one second are clamped at runtime.
    /// Default <c>0</c> = no intrabar delay (subject only to poll cadence and per-user throttle). Per-user{' '}
    /// <c>Users.FavoriteMlAutomationMinSecondsAfterBarOpen</c> overrides this when set (SPA + <c>PUT …/favorite-ml-automation</c>).
    /// When the user sets an <strong>N</strong>-minute cadence (<c>FavoriteMlAutomationPollIntervalSeconds</c> &gt; 0), the worker skips this gate entirely.
    /// </summary>
    public int MinSecondsAfterBarOpenForAutomation { get; set; }

    /// <summary>IANA or Windows TZ id (e.g. <c>Asia/Kolkata</c>, <c>India Standard Time</c>).</summary>
    public string ReportTimeZoneId { get; set; } = "Asia/Kolkata";

    /// <summary>Local hour (0–23) to start the daily CSV + pie send window (same timezone as <see cref="ReportTimeZoneId"/>; default 23 = 11 PM).</summary>
    public int ReportLocalHour { get; set; } = 23;

    /// <summary>Local minute (0–59); default 30 → 11:30 PM when <see cref="ReportTimeZoneId"/> is IST.</summary>
    public int ReportLocalMinute { get; set; } = 30;

    /// <summary>Fallback chart interval when user has never saved instruments chart settings.</summary>
    public string DefaultChartInterval { get; set; } = "5m";

    /// <summary>
    /// Comma-separated subset of registry model ids (GET /api/v1/predictions/price-direction/models). When null, empty,
    /// or whitespace, automation runs <strong>every</strong> registered engine on each eligible favorite bar (LightGBM uses its own table).
    /// </summary>
    public string? PredictionModelId { get; set; }

    /// <summary>Max pending rows to scan for resolution per user per tick.</summary>
    public int MaxPendingResolutionBatch { get; set; } = 2_000;

    /// <summary>
    /// When true (default), each automation prediction runs three sliding-window inferences on the same latest bar
    /// (drops 0, 1, then 2 oldest candles). Stored <c>direction</c> is the majority (e.g. up, up, down → up); <c>detail</c> begins with a compact
    /// <c>[b3 …]</c> tag. Requires at least <c>MinCandlesRequired + 2</c> bars; otherwise falls back to a single inference. Reports may include a direction-vote pie.
    /// </summary>
    public bool BestOfThreeEnabled { get; set; } = true;

    /// <summary>
    /// When true, skips only the automated <strong>new prediction</strong> pass (per favorite/engine). Pending resolutions,
    /// nightly EOD email, interactive REST predictions, and broker/live ticks are unchanged.
    /// </summary>
    public bool QuietHoursEnabled { get; set; } = true;

    /// <summary>Local hour when automation stops (inclusive).</summary>
    public int QuietHoursStartLocalHour { get; set; } = 23;

    /// <summary>Local minute when automation stops (inclusive); default <c>25</c> → 11:25 PM IST when TZ is Kolkata.</summary>
    public int QuietHoursStartLocalMinute { get; set; } = 25;

    /// <summary>Local hour when automation resumes (exclusive).</summary>
    public int QuietHoursEndLocalHour { get; set; } = 8;

    /// <summary>Local minute when automation resumes (exclusive); → 8:00 AM local with minute <c>0</c>.</summary>
    public int QuietHoursEndLocalMinute { get; set; } = 0;

    /// <summary>
    /// When true (default), local Saturday/Sunday in <see cref="ReportTimeZoneId"/> skip <strong>new</strong> scheduled
    /// automation predictions for the whole day (pending resolution still runs). Set false for 24/7 markets or tests.
    /// </summary>
    public bool PauseAutomationOnWeekends { get; set; } = true;

    /// <summary>
    /// When true (default), <strong>new</strong> intraday automation is limited to <see cref="TradingSessionStartLocalHour"/>/<see cref="TradingSessionStartLocalMinute"/>
    /// (inclusive) through <see cref="TradingSessionEndLocalHour"/>/<see cref="TradingSessionEndLocalMinute"/> (exclusive) in <see cref="ReportTimeZoneId"/>.
    /// Daily/weekly candles are exempt. See <see cref="SkipTradingSessionForMcxFavorites"/>.
    /// </summary>
    public bool TradingSessionRestrictionsEnabled { get; set; } = true;

    /// <summary>Default NSE-style cash/F&amp;O window (India): 09:15 local open.</summary>
    public int TradingSessionStartLocalHour { get; set; } = 9;

    public int TradingSessionStartLocalMinute { get; set; } = 15;

    /// <summary>Exclusive end (default 15:30): no new intraday predictions at or after this local clock time.</summary>
    public int TradingSessionEndLocalHour { get; set; } = 15;

    public int TradingSessionEndLocalMinute { get; set; } = 30;

    /// <summary>When true (default), favorites on exchange <c>MCX</c> bypass <see cref="TradingSessionRestrictionsEnabled"/>.</summary>
    public bool SkipTradingSessionForMcxFavorites { get; set; } = true;

    /// <summary>
    /// When true (default), only the first <see cref="IstMinuteBoundarySeconds"/> seconds of each local wall-clock minute
    /// (in <see cref="ReportTimeZoneId"/>; IST default) qualify for new predictions—pairs with polling so runs line up with round-minute timestamps.
    /// Does not apply to <strong>N</strong>-minute user cadence. Skipped for <c>1d</c>/<c>1w</c> candles.
    /// </summary>
    public bool IstMinuteBoundaryAlignmentEnabled { get; set; } = true;

    /// <summary>Width of each allowed window at the start of a calendar minute (clamped 5–45).</summary>
    public int IstMinuteBoundarySeconds { get; set; } = 22;
}
