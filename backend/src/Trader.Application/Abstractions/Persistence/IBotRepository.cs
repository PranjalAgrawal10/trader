using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IBotRepository
{
    Task<IReadOnlyList<Bot>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<Bot?> GetAsync(Guid botId, CancellationToken ct = default);
    Task AddAsync(Bot bot, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
