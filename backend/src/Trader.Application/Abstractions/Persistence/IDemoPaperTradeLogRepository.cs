using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IDemoPaperTradeLogRepository
{
    void Add(DemoPaperTradeLog entity);

    Task<IReadOnlyList<DemoPaperTradeLog>> ListRecentByUserAsync(Guid userId, int take, CancellationToken ct = default);

    /// <summary>
    /// For each instrument token, the <see cref="DemoPaperTradeLog.LastPrice"/> of the most recent BUY row (by execution time, then id).
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetLatestBuyLastPriceByInstrumentTokensAsync(
        Guid userId,
        IReadOnlyCollection<string> instrumentTokens,
        CancellationToken ct = default);
}
