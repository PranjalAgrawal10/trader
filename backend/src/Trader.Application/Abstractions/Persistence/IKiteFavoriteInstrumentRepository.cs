using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IKiteFavoriteInstrumentRepository
{
    Task<IReadOnlyList<KiteFavoriteInstrument>> ListByUserAsync(Guid userId, CancellationToken ct = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default);

    Task<KiteFavoriteInstrument?> FindAsync(Guid userId, string instrumentToken, CancellationToken ct = default);

    Task AddAsync(KiteFavoriteInstrument entity, CancellationToken ct = default);

    void Remove(KiteFavoriteInstrument entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}
