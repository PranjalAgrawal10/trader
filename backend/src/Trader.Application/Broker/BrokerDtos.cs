using System.Text.Json;

namespace Trader.Application.Broker;

public sealed record BrokerStatusDto(bool Connected, DateTimeOffset? ConnectedAt, string? Provider);

public sealed record BrokerProviderAvailabilityDto(string Key, string Label, bool Connected);

public sealed record KiteLoginUrlDto(string LoginUrl);

public sealed class GrowwConnectRequestDto
{
    public string? AccessToken { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? Totp { get; set; }
}

public sealed class BrokerSelectionPutDto
{
    public string? Broker { get; set; }
}

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
    int? LotSize,
    decimal? TickSize = null);

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

/// <summary>Kite orderbook row from <c>GET /orders</c> (trading-day scope; includes open/pending/executed/rejected/cancelled).</summary>
public sealed record KiteOrderBookItemDto(
    string OrderId,
    string? ExchangeOrderId,
    string? ParentOrderId,
    string Status,
    string? StatusMessage,
    string? StatusMessageRaw,
    string Tradingsymbol,
    string Exchange,
    string TransactionType,
    string Variety,
    string OrderType,
    string Product,
    string Validity,
    int Quantity,
    int FilledQuantity,
    int PendingQuantity,
    int? CancelledQuantity,
    decimal? Price,
    decimal? TriggerPrice,
    decimal? AveragePrice,
    string? Tag,
    string? OrderTimestamp,
    string? ExchangeUpdateTimestamp);

public sealed record KiteOrderBookDto(IReadOnlyList<KiteOrderBookItemDto> Items);

public sealed record KiteNetPositionDto(
    string Exchange,
    string Tradingsymbol,
    string Product,
    int Quantity);

/// <summary>One segment from Kite <c>GET /user/margins</c> (equity or commodity).</summary>
public sealed record KiteMarginSegmentDto(
    bool Enabled,
    decimal Net,
    decimal AvailableCash,
    decimal LiveBalance,
    decimal OpeningBalance,
    decimal IntradayPayin,
    decimal UtilisedDebits);

/// <summary>Funds and margin snapshot from Kite <c>GET /user/margins</c>.</summary>
public sealed record KiteUserMarginsDto(
    KiteMarginSegmentDto? Equity,
    KiteMarginSegmentDto? Commodity);

public sealed record KiteOrderActionResultDto(string OrderId, string Action, string Message);

/// <summary>Two-leg GTT OCO (stop-loss + target). Percent fields default to 5 when omitted.</summary>
public sealed class KiteGttCreateRequestDto
{
    public string? Exchange { get; set; }
    public string? Tradingsymbol { get; set; }

    /// <summary>Entry side (<c>BUY</c> or <c>SELL</c>); exit side is inferred.</summary>
    public string? EntryTransactionType { get; set; }

    public int Quantity { get; set; }
    public string? Product { get; set; }

    /// <summary>Reference for percent-based SL/target when explicit prices are omitted.</summary>
    public decimal? ReferencePrice { get; set; }

    /// <summary>LTP at placement; fetched from Kite quote when omitted.</summary>
    public decimal? LastPrice { get; set; }

    public decimal? StopLossPrice { get; set; }

    /// <summary>Target / take-profit trigger price.</summary>
    public decimal? TriggerPrice { get; set; }

    public decimal StopLossPercent { get; set; } = 5m;
    public decimal TriggerPercent { get; set; } = 5m;
    public string? Tag { get; set; }
}

public sealed record KiteGttActionResultDto(
    string TriggerId,
    string Action,
    string Message,
    decimal StopLossPrice,
    decimal TargetPrice);

public sealed class KiteOrderCancelRequestDto
{
    public string? Variety { get; set; }
    public string? ParentOrderId { get; set; }
}

public sealed class KiteOrderModifyRequestDto
{
    public string? Variety { get; set; }
    public string? Exchange { get; set; }
    public string? Tradingsymbol { get; set; }
    public string? TransactionType { get; set; }
    public int Quantity { get; set; }
    public string? Product { get; set; }
    public string? OrderType { get; set; }
    public string? Validity { get; set; }
    public decimal? Price { get; set; }
    public decimal? TriggerPrice { get; set; }
    public int? DisclosedQuantity { get; set; }
    public string? Tag { get; set; }
    public int? MarketProtection { get; set; }
}

public sealed class KiteOrderRepeatRequestDto
{
    public string? Variety { get; set; }
}

public sealed class KiteOrderPlaceRequestDto
{
    public string? Broker { get; set; }
    public string? Variety { get; set; }
    public string? Exchange { get; set; }
    public string? Tradingsymbol { get; set; }
    public string? TransactionType { get; set; }
    public int Quantity { get; set; }
    public string? Segment { get; set; }
    public string? Product { get; set; }
    public string? OrderType { get; set; }
    public string? Validity { get; set; }
    public decimal? Price { get; set; }
    public decimal? TriggerPrice { get; set; }
    public int? DisclosedQuantity { get; set; }
    public string? Tag { get; set; }
    public int? MarketProtection { get; set; }
}

public sealed record ScalperSettingsDto(
    string Interval,
    string RangePreset,
    string GraphType,
    bool ShowVolume,
    bool SafeModeEnabled,
    decimal SafeStopLossPoints,
    decimal SafeTriggerPoints,
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
    public bool GttEnabled { get; set; } = true;
}

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

/// <summary>Toggle persisted demo auto-trade (no broker orders).</summary>
public sealed class DemoAutoTradePutDto
{
    public bool Enabled { get; set; }

    /// <summary>Strategy id (<see cref="DemoAutoTradeStrategyIds"/>); omit or null to keep the stored preset.</summary>
    public string? Strategy { get; set; }
}

/// <summary>Manual paper buy/sell at Kite LTP for a Locked for trading instrument (adjusts wallet; no orders).</summary>
public sealed class DemoPaperTradeRequestDto
{
    public string? InstrumentToken { get; set; }

    /// <summary><c>buy</c> or <c>sell</c> (close long).</summary>
    public string? Side { get; set; }

    /// <summary>
    /// Number of exchange lots (Kite <c>lot_size</c> units per lot). Notional per fill ≈ <c>lots × lot_size × LTP</c> (server rounding applies).
    /// JSON property name remains <c>contracts</c> for compatibility.
    /// </summary>
    public int Contracts { get; set; }
}

public sealed record DemoPaperTradeResultDto(
    string InstrumentToken,
    string Tradingsymbol,
    string Exchange,
    string Side,
    /// <summary>Exchange lots (JSON: <c>contracts</c>).</summary>
    int Contracts,
    decimal LastPrice,
    int LotSize,
    decimal CashFlowInr,
    decimal WalletBalanceAfter,
    /// <summary>Open long in lots (same unit as <see cref="Contracts"/>).</summary>
    int OpenContractsAfter);

/// <summary>Append-only manual demo paper fills (newest rows returned first).</summary>
public sealed record DemoPaperTradeHistoryRowDto(
    Guid Id,
    DateTimeOffset ExecutedAtUtc,
    string InstrumentToken,
    string Tradingsymbol,
    string Exchange,
    string Side,
    /// <summary>Exchange lots (JSON: <c>contracts</c>).</summary>
    int Contracts,
    decimal LastPrice,
    int LotSize,
    decimal CashFlowInr,
    decimal WalletBalanceAfter,
    int OpenContractsAfter);

public sealed record DemoPaperOpenBuyMarkerDto(DateTimeOffset BoughtAtUtc, int ContractsRemaining);

public sealed record DemoPaperPositionListItemDto(
    string InstrumentToken,
    string Tradingsymbol,
    string Exchange,
    int? LotSize,
    int OpenContracts,
    IReadOnlyList<DemoPaperOpenBuyMarkerDto> OpenBuys,
    /// <summary>Latest demo BUY fill price from trade logs when <see cref="OpenContracts"/> &gt; 0.</summary>
    decimal? LastBuyPrice);

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
    /// <summary>Distinct instruments in <see cref="KiteTradingLocksListDto"/> used to filter automation rows for this demo.</summary>
    int DemoAutoTradeLockedInstrumentCount,
    bool MayBeTruncated);

/// <summary>One merged automation row scored for hypothetical demo auto-trade (same allocation rules as EOD).</summary>
public sealed record DemoAutoTradeLegRowDto(
    Guid PredictionId,
    DateTimeOffset PredictedAtUtc,
    string InstrumentToken,
    string? Tradingsymbol,
    string? Exchange,
    string Interval,
    string EngineModelId,
    string Direction,
    int Confidence,
    string Outcome,
    decimal RefClose,
    /// <summary>Next bar open when resolved; demo P&amp;L uses this as market-style entry when present.</summary>
    decimal? NextOpen,
    decimal? NextClose,
    /// <summary><c>allocated</c>, <c>pending</c>, or <c>excluded_*</c>.</summary>
    string Status,
    string? StatusDetail,
    decimal AllocatedNotionalInr,
    /// <summary>Kite-style contract multiplier from Locked for trading. <strong>0</strong> when using legacy fractional-notional demo math (no contract map).</summary>
    int InstrumentLotMultiplier,
    /// <summary>Integer contracts sized from the INR allocation using the contract multiplier. Zero when unallocated.</summary>
    int DemoWholeLotsTraded,
    /// <summary>Approximate ₹ exposure at hypothetical entry (<c>entry × InstrumentLotMultiplier × DemoWholeLotsTraded</c>); legacy mode uses allocation INR only.</summary>
    decimal CommittedExposureApproxInr,
    /// <summary>For a long/up leg: purchase price at entry; for a short/down leg: exit cover (buy) price.</summary>
    decimal? HypotheticalBuyPrice,
    /// <summary>For a long/up leg: sale price at exit; for a short/down leg: short-sale price at entry.</summary>
    decimal? HypotheticalSellPrice,
    decimal LegGrossPnlInr,
    decimal LegFeesInr,
    decimal LegNetPnlInr);

/// <summary>Live-refresh payload: per-row hypothetical demo legs for the report calendar day (trading-lock filter applied server-side).</summary>
public sealed record DemoAutoTradeTodayLegsDto(
    DateTimeOffset GeneratedAtUtc,
    DateOnly ReportDate,
    string ReportTimeZoneId,
    bool DemoAutoTradeEnabled,
    string DemoAutoTradeStrategy,
    string DemoAutoTradeStrategyTitle,
    decimal DemoNotionalInr,
    int DemoAutoTradeLockedInstrumentCount,
    bool DemoAutoTradeChargesEnabled,
    decimal DemoAutoTradeRoundTripFlatInrPerLeg,
    decimal DemoAutoTradeRoundTripTurnoverBps,
    IReadOnlyList<DemoAutoTradeLegRowDto> Legs,
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
    /// <summary>Distinct instruments in <see cref="KiteTradingLocksListDto"/> used to filter automation rows for this demo.</summary>
    int DemoAutoTradeLockedInstrumentCount,
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

/// <summary>Updates saved zoom for one instrument. Prefer <see cref="VisibleFraction"/>; <c>null</c> on both clears.</summary>
public sealed record KiteInstrumentsChartZoomPutDto(string InstrumentToken, int? VisibleBars = null, double? VisibleFraction = null);

/// <summary>Sets or clears a per-instrument candle interval override; <c>null</c> <see cref="Interval"/> uses the page default for that token.</summary>
public sealed record KiteInstrumentsChartIntervalPutDto(string InstrumentToken, string? Interval);
