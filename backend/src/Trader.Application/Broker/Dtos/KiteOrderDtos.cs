namespace Trader.Application.Broker;

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
