using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class DemoPaperBuyLegRepository : IDemoPaperBuyLegRepository
{
    private readonly TraderDbContext _db;

    public DemoPaperBuyLegRepository(TraderDbContext db)
    {
        _db = db;
    }

    public void Add(DemoPaperBuyLeg entity) => _db.DemoPaperBuyLegs.Add(entity);

    public async Task ApplyFifoSellAsync(Guid userId, string instrumentToken, int contracts, CancellationToken ct = default)
    {
        if (contracts < 1)
            throw new ArgumentOutOfRangeException(nameof(contracts));

        var legs = await _db.DemoPaperBuyLegs
            .Where(x =>
                x.UserId == userId &&
                string.Equals(x.InstrumentToken, instrumentToken, StringComparison.Ordinal) &&
                x.ContractsRemaining > 0)
            .OrderBy(x => x.BoughtAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var unmatched = contracts;
        foreach (var leg in legs)
        {
            if (unmatched <= 0)
                break;
            var take = Math.Min(leg.ContractsRemaining, unmatched);
            leg.ContractsRemaining -= take;
            unmatched -= take;
            if (leg.ContractsRemaining <= 0)
                _db.DemoPaperBuyLegs.Remove(leg);
        }

        if (unmatched != 0)
            throw new InvalidOperationException(
                $"Demo paper FIFO sell mismatch: token {instrumentToken}, could not consume {contracts} contracts ({unmatched} unmatched).");
    }

    public async Task<IReadOnlyList<DemoPaperBuyLeg>> ListOpenByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.DemoPaperBuyLegs.AsNoTracking()
            .Where(x => x.UserId == userId && x.ContractsRemaining > 0)
            .OrderBy(x => x.InstrumentToken).ThenBy(x => x.BoughtAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
