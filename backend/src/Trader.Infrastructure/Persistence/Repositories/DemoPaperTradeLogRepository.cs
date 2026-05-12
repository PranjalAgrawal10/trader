using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;
using Trader.Domain.Entities;

namespace Trader.Infrastructure.Persistence.Repositories;

public sealed class DemoPaperTradeLogRepository : IDemoPaperTradeLogRepository
{
    private readonly TraderDbContext _db;

    public DemoPaperTradeLogRepository(TraderDbContext db)
    {
        _db = db;
    }

    public void Add(DemoPaperTradeLog entity) => _db.DemoPaperTradeLogs.Add(entity);

    public async Task<IReadOnlyList<DemoPaperTradeLog>> ListRecentByUserAsync(Guid userId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 2000);
        return await _db.DemoPaperTradeLogs.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ExecutedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, decimal>> GetLatestBuyLastPriceByInstrumentTokensAsync(
        Guid userId,
        IReadOnlyCollection<string> instrumentTokens,
        CancellationToken ct = default)
    {
        if (instrumentTokens.Count == 0)
            return new Dictionary<string, decimal>(StringComparer.Ordinal);

        var tokenList = instrumentTokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tokenList.Count == 0)
            return new Dictionary<string, decimal>(StringComparer.Ordinal);

        var rows = await _db.DemoPaperTradeLogs.AsNoTracking()
            .Where(x => x.UserId == userId && x.Side == "buy" && tokenList.Contains(x.InstrumentToken))
            .OrderByDescending(x => x.ExecutedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.InstrumentToken, x.LastPrice })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dict = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!dict.ContainsKey(row.InstrumentToken))
                dict[row.InstrumentToken] = row.LastPrice;
        }

        return dict;
    }
}
