using Trader.Application.Prediction;
using Trader.Domain.Entities;

namespace Trader.Application.Abstractions.Persistence;

public interface IMlLightGbmTripleBarrierPredictionRepository
{
    Task<MlLightGbmTripleBarrierPrediction?> FindTrackedAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<bool> HasPendingForRefBarAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        CancellationToken ct = default);

    Task<bool> HasPendingForRefBarAndEngineModelAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        string engineModelId,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlLightGbmTripleBarrierPrediction>> ListPendingAsync(
        Guid userId,
        int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlLightGbmTripleBarrierPrediction>> ListPredictedBetweenAsync(
        Guid userId,
        DateTimeOffset startUtcInclusive,
        DateTimeOffset endUtcExclusive,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlAutomationPredictionListItemDto>> ListAutomationRecentAsync(
        Guid userId,
        int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlLightGbmTripleBarrierPrediction>> ListForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        int take,
        CancellationToken ct = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(MlLightGbmTripleBarrierPrediction entity, CancellationToken ct = default);

    Task PruneForUserAsync(Guid userId, int maxTotal, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
