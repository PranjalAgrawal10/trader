using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class BotRepository : IBotRepository
{
    private readonly TraderDbContext _db;

    public BotRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Bot bot, CancellationToken ct = default) =>
        await _db.Bots.AddAsync(bot, ct);

    public Task<Bot?> GetAsync(Guid botId, CancellationToken ct = default) =>
        _db.Bots.FirstOrDefaultAsync(b => b.Id == botId, ct);

    public async Task<IReadOnlyList<Bot>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _db.Bots.Where(b => b.UserId == userId).OrderByDescending(b => b.StartedAt).ToListAsync(ct);
        return list;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
