namespace Trader.Application.Trades;

public sealed record TradingOrderResponse(
    Guid Id,
    Guid BotId,
    string ExternalId,
    string Status,
    DateTimeOffset CreatedAt);
