using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class KiteTradingLockInstrumentRepository : IKiteTradingLockInstrumentRepository
{
    private readonly TraderDbContext _db;

    public KiteTradingLockInstrumentRepository(TraderDbContext db) => _db = db;

    public async Task<IReadOnlyList<KiteTradingLockInstrument>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _db.KiteTradingLockInstruments
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return list;
    }

    public Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.KiteTradingLockInstruments.CountAsync(x => x.UserId == userId, ct);

    public Task<KiteTradingLockInstrument?> FindAsync(Guid userId, string instrumentToken, CancellationToken ct = default) =>
        _db.KiteTradingLockInstruments.FirstOrDefaultAsync(
            x => x.UserId == userId && x.InstrumentToken == instrumentToken,
            ct);

    public async Task AddAsync(KiteTradingLockInstrument entity, CancellationToken ct = default) =>
        await _db.KiteTradingLockInstruments.AddAsync(entity, ct);

    public void Remove(KiteTradingLockInstrument entity) =>
        _db.KiteTradingLockInstruments.Remove(entity);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
