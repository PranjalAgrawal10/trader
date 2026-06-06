using Trader.Application.Bots;

namespace Trader.Application.Trades;

public interface ITradeService
{
    Task<IReadOnlyList<TradeResponse>> ListAsync(Guid userId, Guid? botId, CancellationToken ct = default);
    Task<IReadOnlyList<TradingOrderResponse>> ListOrdersAsync(Guid userId, Guid? botId, CancellationToken ct = default);
}
