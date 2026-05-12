using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IDemoPaperBuyLegRepository
{
    void Add(DemoPaperBuyLeg entity);

    /// <summary>Open legs for the user (remaining contracts only), unordered — group in the caller.</summary>
    Task<IReadOnlyList<DemoPaperBuyLeg>> ListOpenByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Reduces/removes BUY legs FIFO for a sell. Loads tracked entities.</summary>
    Task ApplyFifoSellAsync(Guid userId, string instrumentToken, int contracts, CancellationToken ct = default);
}
