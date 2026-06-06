using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class TradingOrderRepository : ITradingOrderRepository
{
    private readonly TraderDbContext _db;

    public TradingOrderRepository(TraderDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Trader.Domain.Entities.TradingOrder>> ListForUserAsync(
        Guid userId,
        Guid? botId,
        CancellationToken ct = default)
    {
        var query = _db.TradingOrders.AsNoTracking().Where(o => o.Bot.UserId == userId);
        if (botId is Guid id)
            query = query.Where(o => o.BotId == id);

        return await query.OrderByDescending(o => o.CreatedAt).Take(1000).ToListAsync(ct);
    }
}
