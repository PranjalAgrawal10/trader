namespace Trader.Application.Broker;

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
