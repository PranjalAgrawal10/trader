using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Prediction;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class MlPriceDirectionPredictionRepository : IMlPriceDirectionPredictionRepository
{
    private const string AutomationSource = PriceDirectionPredictionService.SourceAutomation;

    private readonly TraderDbContext _db;

    public MlPriceDirectionPredictionRepository(TraderDbContext db)
    {
        _db = db;
    }

    public Task<MlPriceDirectionPrediction?> FindTrackedAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        _db.MlPriceDirectionPredictions.FirstOrDefaultAsync(x => x.UserId == userId && x.Id == id, ct);

    public Task<bool> HasPendingForRefBarAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        CancellationToken ct = default) =>
        _db.MlPriceDirectionPredictions.AnyAsync(
            x => x.UserId == userId
                && x.InstrumentToken == instrumentToken
                && x.Interval == interval
                && x.RefBarTimeUtc == refBarTimeUtc
                && x.Outcome == "pending",
            ct);

    public Task<bool> HasPendingForRefBarAndEngineModelAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        string engineModelId,
        CancellationToken ct = default)
    {
        var eid = engineModelId.Trim();
        return _db.MlPriceDirectionPredictions.AnyAsync(
            x => x.UserId == userId
                && x.InstrumentToken == instrumentToken
                && x.Interval == interval
                && x.RefBarTimeUtc == refBarTimeUtc
                && x.Outcome == "pending"
                && (x.EngineModelId == eid || (x.EngineModelId == null && x.ModelId == eid)),
            ct);
    }

    public async Task<IReadOnlyList<MlPriceDirectionPrediction>> ListPendingAsync(
        Guid userId,
        int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 10_000);
        return await _db.MlPriceDirectionPredictions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Outcome == "pending")
            .OrderBy(x => x.PredictedAtUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MlPriceDirectionPrediction>> ListPredictedBetweenAsync(
        Guid userId,
        DateTimeOffset startUtcInclusive,
        DateTimeOffset endUtcExclusive,
        CancellationToken ct = default)
    {
        return await _db.MlPriceDirectionPredictions
            .AsNoTracking()
            .Where(x => x.UserId == userId
                && x.PredictedAtUtc >= startUtcInclusive
                && x.PredictedAtUtc < endUtcExclusive)
            .OrderBy(x => x.PredictedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MlAutomationPredictionListItemDto>> ListAutomationRecentAsync(
        Guid userId,
        int take,
        DateTimeOffset? predictedAtFromUtcInclusive = null,
        DateTimeOffset? predictedAtToUtcExclusive = null,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, PriceDirectionPredictionService.MaxAutomationHistoryTake);
        var preds = _db.MlPriceDirectionPredictions.AsNoTracking()
            .Where(p => p.UserId == userId && p.Source == AutomationSource);
        if (predictedAtFromUtcInclusive.HasValue)
        {
            var from = predictedAtFromUtcInclusive.Value;
            var to = predictedAtToUtcExclusive!.Value;
            preds = preds.Where(p => p.PredictedAtUtc >= from && p.PredictedAtUtc < to);
        }

        var q =
            from p in preds
            join f in _db.KiteFavoriteInstruments.AsNoTracking()
                on new { p.UserId, Token = p.InstrumentToken } equals new { f.UserId, Token = f.InstrumentToken } into fav
            from f in fav.DefaultIfEmpty()
            orderby p.PredictedAtUtc descending
            select new MlAutomationPredictionListItemDto(
                p.Id,
                p.PredictedAtUtc,
                p.InstrumentToken,
                f == null ? null : f.Tradingsymbol,
                f == null ? null : f.Exchange,
                p.Interval,
                p.RefBarTimeUtc,
                p.RefClose,
                p.Direction,
                p.Confidence,
                p.Outcome,
                p.NextBarTimeUtc,
                p.NextOpen,
                p.NextClose,
                p.EngineModelId ?? p.ModelId);
        return await q.Take(take).ToListAsync(ct).ConfigureAwait(false);
    }

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
