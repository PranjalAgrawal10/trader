using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface INiftyOpenAutoTradeRunRepository
{
    Task AddAsync(NiftyOpenAutoTradeRun run, CancellationToken ct = default);

    Task<NiftyOpenAutoTradeRun?> GetLatestByUserAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<NiftyOpenAutoTradeRun>> ListByUserAsync(Guid userId, int take, CancellationToken ct = default);

    /// <summary>Tracked rows with an active trailing GTT (any session day).</summary>
    Task<IReadOnlyList<NiftyOpenAutoTradeRun>> ListActiveTrailingAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
