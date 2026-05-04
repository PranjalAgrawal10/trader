using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IStrategyRepository
{
    Task<IReadOnlyList<Strategy>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<Strategy?> GetAsync(Guid strategyId, CancellationToken ct = default);
    Task AddAsync(Strategy strategy, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    void Remove(Strategy strategy);
}
