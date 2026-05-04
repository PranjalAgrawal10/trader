using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class TradeRepository : ITradeRepository
{
    private readonly TraderDbContext _db;

    public TradeRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Trader.Domain.Entities.Trade>> ListForUserAsync(Guid userId, Guid? botId, CancellationToken ct = default)
    {
        var query = _db.Trades.AsNoTracking().Where(t => t.Bot.UserId == userId);
        if (botId is Guid id)
            query = query.Where(t => t.BotId == id);

        return await query.OrderByDescending(t => t.ExecutedAt).Take(500).ToListAsync(ct);
    }
}
