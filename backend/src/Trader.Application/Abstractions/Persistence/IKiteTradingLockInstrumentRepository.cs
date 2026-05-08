using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IKiteTradingLockInstrumentRepository
{
    Task<IReadOnlyList<KiteTradingLockInstrument>> ListByUserAsync(Guid userId, CancellationToken ct = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default);

    Task<KiteTradingLockInstrument?> FindAsync(Guid userId, string instrumentToken, CancellationToken ct = default);

    Task AddAsync(KiteTradingLockInstrument entity, CancellationToken ct = default);

    void Remove(KiteTradingLockInstrument entity);

    Task SaveChangesAsync(CancellationToken ct = default);
}
