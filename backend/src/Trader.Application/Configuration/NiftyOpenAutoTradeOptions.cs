namespace Trader.Application.Configuration;

/// <summary>Host settings for live NIFTY ATM MIS auto-entry at market open (09:15 IST) with trailing GTT stop-loss.</summary>
public sealed class NiftyOpenAutoTradeOptions
{
    public const string SectionName = "NiftyOpenAutoTrade";

    /// <summary>Global kill switch for the background worker.</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public string TimeZoneId { get; set; } = "Asia/Kolkata";

    public int FireLocalHour { get; set; } = 9;

    public int FireLocalMinute { get; set; } = 15;

    /// <summary>Seconds after 09:15:00 IST during which a pending plan may still fire (covers poll jitter).</summary>
    public int FireWindowSeconds { get; set; } = 120;

    public bool PauseOnWeekends { get; set; } = true;

    /// <summary>
    /// Trailing stop gap in option premium points (₹). Initial SL = entry − this; SL rises with peak LTP − this.
    /// </summary>
    public decimal TrailingStopLossPoints { get; set; } = 5m;

    /// <summary>Local hour (IST) after which open-auto trail management stops for the session (MIS square-off remains on broker).</summary>
    public int TrailEndLocalHour { get; set; } = 15;

    /// <summary>Local minute paired with <see cref="TrailEndLocalHour"/>.</summary>
    public int TrailEndLocalMinute { get; set; } = 25;

    public int DefaultMaxLots { get; set; } = 5;

    public int AbsoluteMaxLots { get; set; } = 10;

    /// <summary>Fraction of Kite available cash to allocate toward option premium (0–1).</summary>
    public decimal BalanceUtilizationFraction { get; set; } = 0.95m;

    /// <summary>How many listed strikes above/below ATM to try when 1 ATM lot is unaffordable.</summary>
    public int MaxStrikeStepsAwayFromAtm { get; set; } = 3;

    public string Product { get; set; } = "MIS";

    public string SpotSearchQuery { get; set; } = "NIFTY 50";

    public string OptionSearchQuery { get; set; } = "NIFTY";

    public string PreferredSpotExchange { get; set; } = "NSE";

    public string OrderTag { get; set; } = "nifty-open-auto";
}
