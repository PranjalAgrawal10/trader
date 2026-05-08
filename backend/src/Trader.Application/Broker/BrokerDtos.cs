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
    bool? MlAutomationEnabled = null);

/// <summary>Toggle background ML runs for favorite instruments (per user).</summary>
public sealed record FavoriteMlAutomationPutDto(bool Enabled);

/// <summary>Updates saved visible bar count for one instrument; <c>null</c> <see cref="VisibleBars"/> clears zoom for that token.</summary>
public sealed record KiteInstrumentsChartZoomPutDto(string InstrumentToken, int? VisibleBars);

/// <summary>Sets or clears a per-instrument candle interval override; <c>null</c> <see cref="Interval"/> uses the page default for that token.</summary>
public sealed record KiteInstrumentsChartIntervalPutDto(string InstrumentToken, string? Interval);
