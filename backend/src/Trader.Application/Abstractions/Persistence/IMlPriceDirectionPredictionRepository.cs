using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IMlPriceDirectionPredictionRepository
{
    Task<MlPriceDirectionPrediction?> FindTrackedAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<bool> HasPendingForRefBarAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlPriceDirectionPrediction>> ListPendingAsync(
        Guid userId,
        int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlPriceDirectionPrediction>> ListPredictedBetweenAsync(
        Guid userId,
        DateTimeOffset startUtcInclusive,
        DateTimeOffset endUtcExclusive,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlPriceDirectionPrediction>> ListForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        int take,
        CancellationToken ct = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(MlPriceDirectionPrediction entity, CancellationToken ct = default);

    /// <summary>Deletes oldest rows for the user until count is at most <paramref name="maxTotal"/>.</summary>
    Task PruneForUserAsync(Guid userId, int maxTotal, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
