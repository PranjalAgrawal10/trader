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

/// <summary>F&amp;O (NFO+BFO) or MCX only.</summary>
public enum KiteInstrumentSearchSegment
{
    Fno,
    Mcx,
}

public sealed record KiteInstrumentSearchDto(
    IReadOnlyList<KiteInstrumentListItemDto> Items,
    bool ScanTruncated);

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
    decimal? Ema21 = null);

/// <summary>Saved Kite instruments for the signed-in user (F&amp;O / MCX).</summary>
public sealed record KiteFavoriteInstrumentsListDto(IReadOnlyList<KiteInstrumentListItemDto> Items);

/// <summary>Persisted Kite instruments page chart controls (interval, range preset, chart type, optional per-instrument zoom).</summary>
public sealed record KiteInstrumentsChartSettingsDto(
    string? Interval,
    string? RangePreset,
    string? GraphType,
    Dictionary<string, int>? ZoomByInstrumentToken = null);

/// <summary>Updates saved visible bar count for one instrument; <c>null</c> <see cref="VisibleBars"/> clears zoom for that token.</summary>
public sealed record KiteInstrumentsChartZoomPutDto(string InstrumentToken, int? VisibleBars);
