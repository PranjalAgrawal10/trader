using System.Text.Json;

namespace Trader.Application.Broker;

public sealed record BrokerStatusDto(bool Connected, DateTimeOffset? ConnectedAt, string? Provider);

public sealed record KiteLoginUrlDto(string LoginUrl);

/// <summary>
/// <see cref="LoginUrl"/> goes to the client; <see cref="PendingOAuthStateKey"/> mirrors the OAuth <c>state</c> query (short server-side key) for the HttpOnly cookie fallback.
/// </summary>
public sealed record KiteLoginUrlBuildResult(string LoginUrl, string PendingOAuthStateKey);

public sealed record KiteInstrumentListItemDto(
    string InstrumentToken,
    string Tradingsymbol,
    string Exchange,
    string? Name,
    string? InstrumentType,
    string? Segment,
    string? Expiry,
    decimal? Strike,
    int? LotSize);

public sealed record KiteFnoCommodityListsDto(
    IReadOnlyList<KiteInstrumentListItemDto> Fno,
    IReadOnlyList<KiteInstrumentListItemDto> Commodities,
    bool FnoTruncated,
    bool CommoditiesTruncated);

/// <summary>F&amp;O (NFO+BFO), MCX, NSE/BSE spot (cash <c>EQ</c> + indices), or all three merged.</summary>
public enum KiteInstrumentSearchSegment
{
    Fno,
    Mcx,
    Spot,
    All,
}

public sealed record KiteInstrumentSearchDto(
    IReadOnlyList<KiteInstrumentListItemDto> Items,
    bool ScanTruncated);

public sealed record KiteHistoricalOverlaysDto(
    IReadOnlyList<KiteHistoricalOverlayPointDto> Points,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>OHL-only candle for fast chart payloads; pair with <see cref="KiteHistoricalOverlaysDto"/>.</summary>
public sealed record KiteHistoricalOhlcvOnlyCandleDto(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

public sealed record KiteHistoricalOhlcvOnlyDto(
    IReadOnlyList<KiteHistoricalOhlcvOnlyCandleDto> Candles,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>Batch OHLC-only response across multiple requested intervals.</summary>
public sealed record KiteHistoricalOhlcvMultiDto(
    IReadOnlyList<KiteHistoricalOhlcvOnlyDto> Items);

public sealed record KiteHistoricalOverlayPointDto(
    DateTimeOffset Time,
    decimal? Sma20,
    decimal? Ema9,
    decimal? Ema21,
    decimal? SrSupport,
    decimal? SrResistance);

/// <summary>LTP-style snapshot via Kite quote/ohlc (small payload; cache ~5s server-side).</summary>
public sealed record KiteInstrumentLiveQuoteDto(
    string Exchange,
    string Tradingsymbol,
    decimal LastPrice,
    decimal PreviousClose);

/// <summary>OHLCV series from Kite historical API (possibly after server-side resampling for 2m / 4m).</summary>
public sealed record KiteHistoricalCandlesDto(
    IReadOnlyList<KiteHistoricalCandlePointDto> Candles,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To);

public sealed record KiteHistoricalCandlePointDto(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    /// <summary>SMA(20) on close; null when insufficient left history (client chart uses extended Kite fetch to warm up).</summary>
    decimal? Sma20 = null,
    decimal? Ema9 = null,
    decimal? Ema21 = null,
    /// <summary>Trailing min low over a 20-bar window (same length as SMA period).</summary>
    decimal? SrSupport = null,
    /// <summary>Trailing max high over a 20-bar window (same length as SMA period).</summary>
    decimal? SrResistance = null);

/// <summary>Saved Kite instruments for the signed-in user (F&amp;O / MCX).</summary>
public sealed record KiteFavoriteInstrumentsListDto(IReadOnlyList<KiteInstrumentListItemDto> Items);

public sealed record KiteTradingLocksListDto(IReadOnlyList<KiteInstrumentListItemDto> Items);

/// <summary>Top movers among the capped F&amp;O+MCX instrument preview (% gain vs prior close).</summary>
public sealed record KiteTodayTopPerformersDto(IReadOnlyList<KiteInstrumentMoverDto> Items, string Basis);

public sealed record KiteInstrumentMoverDto(
    KiteInstrumentListItemDto Instrument,
    decimal LastPrice,
    decimal PreviousClose,
    decimal ChangePercent);

/// <summary>Persisted Kite instruments page chart controls (interval, range preset, chart type, optional per-instrument zoom).</summary>
public sealed record KiteInstrumentsChartSettingsDto(
    string? Interval,
    string? RangePreset,
    string? GraphType,
    Dictionary<string, int>? ZoomByInstrumentToken = null,
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
    /// <summary>Fixed demo portfolio notional in INR for UI and EOD math.</summary>
    decimal DemoAutoTradeNotionalInr = DemoAutoTradeEodSummaryCalculator.DefaultNotionalInr,
    /// <summary>Allocation preset slug (<see cref="DemoAutoTradeStrategyIds"/>).</summary>
    string? DemoAutoTradeStrategy = null);

/// <summary>Toggle persisted demo auto-trade (no broker orders).</summary>
public sealed class DemoAutoTradePutDto
{
    public bool Enabled { get; set; }

    /// <summary>Strategy id (<see cref="DemoAutoTradeStrategyIds"/>); omit or null to keep the stored preset.</summary>
    public string? Strategy { get; set; }
}

/// <summary>Hypothetical same-calendar-day (report TZ) outcome from merged automation rows.</summary>
public sealed record DemoAutoTradeEodSummaryDto(
    DateOnly ReportDateIst,
    string ReportTimeZoneId,
    bool DemoAutoTradeEnabled,
    string DemoAutoTradeStrategy,
    string DemoAutoTradeStrategyTitle,
    decimal DemoNotionalInr,
    int TotalSignals,
    int PendingSignals,
    int CorrectOutcomes,
    int WrongOutcomes,
    int SkippedNoNextClose,
    int DirectionalTradeableLegs,
    int AllocatedLegsForPnl,
    int SkippedLowConfidenceLegs,
    bool DemoAutoTradeChargesEnabled,
    decimal DemoAutoTradeRoundTripFlatInrPerLeg,
    decimal DemoAutoTradeRoundTripTurnoverBps,
    decimal HypotheticalGrossPnlInr,
    decimal HypotheticalChargesInr,
    decimal HypotheticalTotalPnlInr,
    string PnlAllocationNote,
    bool MayBeTruncated);

/// <summary>One calendar day in report TZ within <see cref="DemoAutoTradeFullReportDto"/>.</summary>
public sealed record DemoAutoTradeFullReportDailyDto(
    DateOnly ReportDate,
    int TotalSignals,
    int PendingSignals,
    int CorrectOutcomes,
    int WrongOutcomes,
    int SkippedNoNextClose,
    int DirectionalTradeableLegs,
    int AllocatedLegsForPnl,
    int SkippedLowConfidenceLegs,
    decimal HypotheticalGrossPnlInr,
    decimal HypotheticalChargesInr,
    decimal HypotheticalTotalPnlInr,
    string PnlAllocationNote);

/// <summary>Counts for a slice (engine or interval) over the report window.</summary>
public sealed record DemoAutoTradeFullReportSliceCountsDto(
    string Key,
    int Total,
    int Pending,
    int Correct,
    int Wrong);

/// <summary>
/// Hypothetical demo auto-trade report: settings, per-day P&amp;L from automation rows, aggregates, and outcome/direction slices.
/// No broker orders; same math as <see cref="DemoAutoTradeEodSummaryDto"/> per day.
/// </summary>
public sealed record DemoAutoTradeFullReportDto(
    DateTimeOffset GeneratedAtUtc,
    string ReportTimeZoneId,
    DateTimeOffset FromUtcInclusive,
    DateTimeOffset ToUtcExclusive,
    string ReportRangeSummary,
    bool DemoAutoTradeEnabled,
    bool FavoriteMlAutomationEnabled,
    string DemoAutoTradeStrategy,
    string DemoAutoTradeStrategyTitle,
    decimal DemoNotionalInrPerDay,
    bool DemoAutoTradeChargesEnabled,
    decimal DemoAutoTradeRoundTripFlatInrPerLeg,
    decimal DemoAutoTradeRoundTripTurnoverBps,
    IReadOnlyList<DemoAutoTradeFullReportDailyDto> DailySummaries,
    int TotalSignalsInRange,
    int PendingSignalsInRange,
    int CorrectOutcomesInRange,
    int WrongOutcomesInRange,
    int DirectionalTradeableLegsInRange,
    decimal HypotheticalGrossPnlInrSummedDays,
    decimal HypotheticalChargesInrSummedDays,
    decimal HypotheticalTotalPnlInrSummedDays,
    int DirectionCountUp,
    int DirectionCountDown,
    int DirectionCountNeutral,
    IReadOnlyList<DemoAutoTradeFullReportSliceCountsDto> OutcomesByEngine,
    IReadOnlyList<DemoAutoTradeFullReportSliceCountsDto> OutcomesByInterval,
    string Disclaimer,
    bool MayBeTruncated);

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

/// <summary>Updates saved visible bar count for one instrument; <c>null</c> <see cref="VisibleBars"/> clears zoom for that token.</summary>
public sealed record KiteInstrumentsChartZoomPutDto(string InstrumentToken, int? VisibleBars);

/// <summary>Sets or clears a per-instrument candle interval override; <c>null</c> <see cref="Interval"/> uses the page default for that token.</summary>
public sealed record KiteInstrumentsChartIntervalPutDto(string InstrumentToken, string? Interval);
