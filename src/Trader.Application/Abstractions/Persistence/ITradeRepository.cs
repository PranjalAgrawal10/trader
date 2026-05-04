using TradeEntity = Trader.Domain.Entities.Trade;

namespace Trader.Application.Abstractions.Persistence;

public interface ITradeRepository
{
    Task<IReadOnlyList<TradeEntity>> ListForUserAsync(Guid userId, Guid? botId, CancellationToken ct = default);
}
