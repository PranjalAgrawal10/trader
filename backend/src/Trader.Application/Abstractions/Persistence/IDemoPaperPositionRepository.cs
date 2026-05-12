using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IDemoPaperPositionRepository
{
    Task<DemoPaperPosition?> FindByUserAndTokenAsync(Guid userId, string instrumentToken, CancellationToken ct = default);

    Task<IReadOnlyList<DemoPaperPosition>> ListByUserAsync(Guid userId, CancellationToken ct = default);

    void Add(DemoPaperPosition entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}
