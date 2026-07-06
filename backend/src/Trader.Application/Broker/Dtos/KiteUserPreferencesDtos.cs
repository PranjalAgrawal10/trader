using System.Text.Json;

namespace Trader.Application.Broker;

public sealed record ScalperSettingsDto(
    string Interval,
    string RangePreset,
    string GraphType,
    bool ShowVolume,
    bool SafeModeEnabled,
    decimal SafeStopLossPoints,
    decimal SafeTriggerPoints,
    bool GttLossEnabled,
    bool GttProfitEnabled,
    /// <summary><c>true</c> when either leg is enabled (legacy clients).</summary>
    bool GttEnabled);

public sealed class ScalperSettingsPutDto
{
    public string? Interval { get; set; }
    public string? RangePreset { get; set; }
    public string? GraphType { get; set; }
    public bool ShowVolume { get; set; }
    public bool SafeModeEnabled { get; set; }
    public decimal SafeStopLossPoints { get; set; }
    public decimal SafeTriggerPoints { get; set; }

    public bool GttLossEnabled { get; set; } = true;
    public bool GttProfitEnabled { get; set; } = true;

    /// <summary>Legacy master toggle; when the new leg flags are omitted from older clients, both legs follow this value.</summary>
    public bool GttEnabled { get; set; } = true;
}

/// <summary>Persisted Kite instruments page chart controls (interval, range preset, chart type, optional per-instrument zoom).</summary>
public sealed record KiteInstrumentsChartSettingsDto(
    string? Interval,
    string? RangePreset,
    string? GraphType,
    /// <summary>Per-token zoom: fractions in (0,1), or legacy whole bar counts (&gt;= 1) saved by older clients.</summary>
    Dictionary<string, double>? ZoomByInstrumentToken = null,
    Dictionary<string, string>? IntervalByInstrumentToken = null,
    bool? MlAutomationEnabled = null,
    string? MlAutomationInterval = null,
    /// <summary>Minimum whole minutes after the previous new-prediction pass started; mirrors DB seconds / 60 (rounded up).</summary>
    int? MlAutomationPollIntervalMinutes = null,
    /// <summary>
    /// Per-user seconds after ref bar open before the first new automation row on that bar; <c>null</c> = use host{' '}
    /// <c>FavoriteMlAutomation:MinSecondsAfterBarOpenForAutomation</c>.
    /// </summary>
    int? MlAutomationMinSecondsAfterBarOpen = null,
    /// <summary>Multi-interval trend checkboxes; omit on PUT to leave stored value unchanged.</summary>
    IReadOnlyList<string>? TrendAnalysisIntervals = null,
    /// <summary>Demo auto-trade intent (no live orders).</summary>
    bool DemoAutoTradeEnabled = false,
    /// <summary>Current wallet balance in INR (paper funds). Drives demo auto-trade allocation size.</summary>
    decimal DemoAutoTradeNotionalInr = DemoAutoTradeEodSummaryCalculator.DefaultNotionalInr,
    /// <summary>Allocation preset slug (<see cref="DemoAutoTradeStrategyIds"/>).</summary>
    string? DemoAutoTradeStrategy = null);

/// <summary>Background favorite-ML automation toggle and optional per-user candle interval / new-pass throttle.</summary>
public sealed class FavoriteMlAutomationPutDto
{
    public bool Enabled { get; set; }

    /// <summary>
    /// When the JSON property is present: empty or whitespace clears the per-user automation interval (server/chart fallback).
    /// When absent or null, the stored interval is left unchanged.
    /// </summary>
    public string? Interval { get; set; }

    /// <summary>
    /// <strong>N</strong> (run cadence): when the JSON property is present, <c>0</c> clears it; <c>1</c>–<c>1440</c> sets minimum whole minutes between <strong>new</strong> pass starts.
    /// When set, passes are driven by this wall-clock spacing (no intrabar wait for the <strong>m</strong>-bar to close). When absent, the stored value is left unchanged.
    /// </summary>
    public int? PollIntervalMinutes { get; set; }

    /// <summary>
    /// When the JSON property is omitted (JSON <c>undefined</c>), the stored per-user value is left unchanged.
    /// When <c>null</c>, clears the per-user override (host <c>FavoriteMlAutomation:MinSecondsAfterBarOpenForAutomation</c> applies).
    /// When a number, must be <c>0</c>–<c>86400</c> (seconds after bar open before new automation predictions on the current ref bar).
    /// </summary>
    public JsonElement MinSecondsAfterBarOpenForAutomation { get; set; }
}

/// <summary>Updates saved zoom for one instrument. Prefer <see cref="VisibleFraction"/>; <c>null</c> on both clears.</summary>
public sealed record KiteInstrumentsChartZoomPutDto(string InstrumentToken, int? VisibleBars = null, double? VisibleFraction = null);

/// <summary>Sets or clears a per-instrument candle interval override; <c>null</c> <see cref="Interval"/> uses the page default for that token.</summary>
public sealed record KiteInstrumentsChartIntervalPutDto(string InstrumentToken, string? Interval);
