using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class KiteFavoriteInstrumentRepository : IKiteFavoriteInstrumentRepository
{
    private readonly TraderDbContext _db;

    public KiteFavoriteInstrumentRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<KiteFavoriteInstrument>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _db.KiteFavoriteInstruments
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return list;
    }

    public Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.KiteFavoriteInstruments.CountAsync(x => x.UserId == userId, ct);

    public Task<KiteFavoriteInstrument?> FindAsync(Guid userId, string instrumentToken, CancellationToken ct = default) =>
        _db.KiteFavoriteInstruments.FirstOrDefaultAsync(
            x => x.UserId == userId && x.InstrumentToken == instrumentToken,
            ct);

    public async Task AddAsync(KiteFavoriteInstrument entity, CancellationToken ct = default)
    {
        await _db.KiteFavoriteInstruments.AddAsync(entity, ct);
    }

    public void Remove(KiteFavoriteInstrument entity) =>
        _db.KiteFavoriteInstruments.Remove(entity);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
