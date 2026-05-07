using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class MlPriceDirectionPredictionRepository : IMlPriceDirectionPredictionRepository
{
    private readonly TraderDbContext _db;

    public MlPriceDirectionPredictionRepository(TraderDbContext db)
    {
        _db = db;
    }

    public Task<MlPriceDirectionPrediction?> FindTrackedAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        _db.MlPriceDirectionPredictions.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);

    public async Task<IReadOnlyList<MlPriceDirectionPrediction>> ListForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        int take,
        CancellationToken ct = default)
    {
        var list = await _db.MlPriceDirectionPredictions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.InstrumentToken == instrumentToken && x.Interval == interval)
            .OrderByDescending(x => x.PredictedAtUtc)
            .Take(take)
            .ToListAsync(ct);
        return list;
    }

    public Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default) =>
        _db.MlPriceDirectionPredictions.CountAsync(x => x.UserId == userId, ct);

    public async Task AddAsync(MlPriceDirectionPrediction entity, CancellationToken ct = default)
    {
        await _db.MlPriceDirectionPredictions.AddAsync(entity, ct);
    }

    public async Task PruneForUserAsync(Guid userId, int maxTotal, CancellationToken ct = default)
    {
        var count = await _db.MlPriceDirectionPredictions.CountAsync(x => x.UserId == userId, ct)
            .ConfigureAwait(false);
        var excess = count - maxTotal;
        if (excess <= 0)
            return;

        var ids = await _db.MlPriceDirectionPredictions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.PredictedAtUtc)
            .Take(excess)
            .Select(x => x.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (ids.Count == 0)
            return;

        await _db.MlPriceDirectionPredictions
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
