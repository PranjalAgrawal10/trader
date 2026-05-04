using Trader.Domain.Enums;

namespace Trader.Application.Bots;

public sealed record BotResponse(
    Guid Id,
    Guid? StrategyId,
    BotStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt);

public sealed record CreateBotRequest(Guid? StrategyId);

public sealed record AssignStrategyRequest(Guid StrategyId);

public sealed record TradeResponse(
    Guid Id,
    Guid BotId,
    string Symbol,
    TradeSide Side,
    decimal Quantity,
    decimal Price,
    decimal? RealizedPnl,
    DateTimeOffset ExecutedAt);
