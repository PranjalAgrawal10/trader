namespace Trader.Application.Broker;

public interface IGrowwTradingClient
{
    Task<GrowwTokenAccessResult> CreateAccessTokenByApprovalAsync(
        string apiKey,
        string apiSecret,
        CancellationToken ct = default);

    Task<GrowwTokenAccessResult> CreateAccessTokenByTotpAsync(
        string apiKey,
        string totp,
        CancellationToken ct = default);

    Task<GrowwOrderActionResult> PlaceOrderAsync(
        GrowwOrderCreateRequest request,
        string accessToken,
        CancellationToken ct = default);

    Task<GrowwPositionsFetchResult> FetchPositionsAsync(
        string accessToken,
        string? segment,
        CancellationToken ct = default);
}

public sealed record GrowwOrderCreateRequest(
    string TradingSymbol,
    int Quantity,
    decimal? Price,
    decimal? TriggerPrice,
    string Validity,
    string Exchange,
    string Segment,
    string Product,
    string OrderType,
    string TransactionType,
    string? OrderReferenceId);

public sealed record GrowwOrderActionResult(
    bool Success,
    string? ErrorMessage,
    string? OrderId,
    string? OrderStatus,
    string? Remark);

public sealed record GrowwPositionsFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<GrowwPositionItem> Items);

public sealed record GrowwPositionItem(
    string Exchange,
    string TradingSymbol,
    string Product,
    int Quantity);

public sealed record GrowwTokenAccessResult(
    bool Success,
    string? ErrorMessage,
    string? AccessToken,
    DateTimeOffset? ExpiresAt,
    string? TokenReferenceId);
