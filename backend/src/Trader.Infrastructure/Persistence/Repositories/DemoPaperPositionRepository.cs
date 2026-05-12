using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class DemoPaperPositionRepository : IDemoPaperPositionRepository
{
    private readonly TraderDbContext _db;

    public DemoPaperPositionRepository(TraderDbContext db)
    {
        _db = db;
    }

    public void Add(DemoPaperPosition entity) => _db.DemoPaperPositions.Add(entity);

    public Task<DemoPaperPosition?> FindByUserAndTokenAsync(Guid userId, string instrumentToken, CancellationToken ct = default) =>
        _db.DemoPaperPositions.FirstOrDefaultAsync(
            x => x.UserId == userId && x.InstrumentToken == instrumentToken,
            ct);

    public async Task<IReadOnlyList<DemoPaperPosition>> ListByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.DemoPaperPositions.AsNoTracking()
            .Where(x => x.UserId == userId && x.OpenContracts > 0)
            .OrderBy(x => x.InstrumentToken)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
