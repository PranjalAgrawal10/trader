using OrderEntity = Trader.Domain.Entities.TradingOrder;

namespace Trader.Application.Abstractions.Persistence;

public interface ITradingOrderRepository
{
    Task<IReadOnlyList<OrderEntity>> ListForUserAsync(Guid userId, Guid? botId, CancellationToken ct = default);
}
