namespace Trader.Application.Configuration;

/// <summary>Host settings for Opening ATM (live MIS BUY at 09:15 IST with ± GTT exits; underlying is per-user).</summary>
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

    /// <summary>Default −ve GTT stop-loss percent of entry premium when the user has not saved a preference.</summary>
    public decimal DefaultStopLossPoints { get; set; } = 5m;

    /// <summary>Default +ve GTT target percent of entry premium when the user has not saved a preference.</summary>
    public decimal DefaultTargetPoints { get; set; } = 5m;

    /// <summary>Legacy trail poll end (still used to clear leftover TrailActive rows).</summary>
    public int TrailEndLocalHour { get; set; } = 15;

    public int TrailEndLocalMinute { get; set; } = 25;

    public int DefaultMaxLots { get; set; } = 10;

    public int AbsoluteMaxLots { get; set; } = 10;

    /// <summary>Fraction of Kite available cash to allocate toward option premium (0–1). Default 1 = max lots from funds.</summary>
    public decimal BalanceUtilizationFraction { get; set; } = 1m;

    /// <summary>How many listed strikes above/below ATM to try when 1 ATM lot is unaffordable.</summary>
    public int MaxStrikeStepsAwayFromAtm { get; set; } = 3;

    public string Product { get; set; } = "MIS";

    /// <summary>Legacy default spot query when underlying catalog is unavailable (prefer per-user underlying).</summary>
    public string SpotSearchQuery { get; set; } = "NIFTY 50";

    /// <summary>Legacy default F&amp;O query when underlying catalog is unavailable (prefer per-user underlying).</summary>
    public string OptionSearchQuery { get; set; } = "NIFTY";

    /// <summary>Legacy preferred spot exchange override for NIFTY only.</summary>
    public string PreferredSpotExchange { get; set; } = "NSE";

    public string OrderTag { get; set; } = "opening-atm";
}
