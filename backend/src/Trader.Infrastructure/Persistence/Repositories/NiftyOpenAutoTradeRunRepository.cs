using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class NiftyOpenAutoTradeRunRepository : INiftyOpenAutoTradeRunRepository
{
    private readonly TraderDbContext _db;

    public NiftyOpenAutoTradeRunRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(NiftyOpenAutoTradeRun run, CancellationToken ct = default) =>
        await _db.NiftyOpenAutoTradeRuns.AddAsync(run, ct).ConfigureAwait(false);

    public Task<NiftyOpenAutoTradeRun?> GetLatestByUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.NiftyOpenAutoTradeRuns.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<NiftyOpenAutoTradeRun>> ListByUserAsync(
        Guid userId,
        int take,
        CancellationToken ct = default)
    {
        return await _db.NiftyOpenAutoTradeRuns.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NiftyOpenAutoTradeRun>> ListActiveTrailingAsync(CancellationToken ct = default)
    {
        return await _db.NiftyOpenAutoTradeRuns
            .Where(r => r.TrailActive)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
