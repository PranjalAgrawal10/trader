using Trader.Application.Abstractions.Persistence;
using Trader.Application.Bots;
using TradeEntity = Trader.Domain.Entities.Trade;

namespace Trader.Application.Trades;

public sealed class TradeService : ITradeService
{
    private readonly ITradeRepository _trades;
    private readonly ITradingOrderRepository _orders;

    public TradeService(ITradeRepository trades, ITradingOrderRepository orders)
    {
        _trades = trades;
        _orders = orders;
    }

    public async Task<IReadOnlyList<TradeResponse>> ListAsync(Guid userId, Guid? botId, CancellationToken ct = default)
    {
        IReadOnlyList<TradeEntity> rows = await _trades.ListForUserAsync(userId, botId, ct);
        return rows.Select(t => new TradeResponse(
            t.Id,
            t.BotId,
            t.Symbol,
            t.Side,
            t.Quantity,
            t.Price,
            t.RealizedPnl,
            t.ExecutedAt)).ToList();
    }

    public async Task<IReadOnlyList<TradingOrderResponse>> ListOrdersAsync(Guid userId, Guid? botId, CancellationToken ct = default)
    {
        var rows = await _orders.ListForUserAsync(userId, botId, ct);
        return rows.Select(o => new TradingOrderResponse(
            o.Id,
            o.BotId,
            o.ExternalId,
            o.Status.ToString(),
            o.CreatedAt)).ToList();
    }
}
