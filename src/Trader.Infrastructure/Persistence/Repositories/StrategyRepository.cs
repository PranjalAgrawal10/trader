using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class StrategyRepository : IStrategyRepository
{
    private readonly TraderDbContext _db;

    public StrategyRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Strategy strategy, CancellationToken ct = default) =>
        await _db.Strategies.AddAsync(strategy, ct);

    public Task<Strategy?> GetAsync(Guid strategyId, CancellationToken ct = default) =>
        _db.Strategies.FirstOrDefaultAsync(s => s.Id == strategyId, ct);

    public async Task<IReadOnlyList<Strategy>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _db.Strategies.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        return list;
    }

    public void Remove(Strategy strategy) =>
        _db.Strategies.Remove(strategy);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
