namespace Trader.Application.Broker;

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

public sealed record KiteTodayTopPerformersDto(IReadOnlyList<KiteInstrumentMoverDto> Items, string Basis);

public sealed record KiteInstrumentMoverDto(
    KiteInstrumentListItemDto Instrument,
    decimal LastPrice,
    decimal PreviousClose,
    decimal ChangePercent);

public sealed record KiteFavoriteInstrumentsListDto(IReadOnlyList<KiteInstrumentListItemDto> Items);

public sealed record KiteTradingLocksListDto(IReadOnlyList<KiteInstrumentListItemDto> Items);
