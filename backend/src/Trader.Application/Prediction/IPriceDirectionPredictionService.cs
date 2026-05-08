using Trader.Application.Broker;

namespace Trader.Application.Prediction;

public interface IPriceDirectionPredictionService
{
    /// <summary>
    /// Loads historical candles via Kite for the authenticated user, then runs the selected <see cref="IPriceDirectionPredictionEngine"/>.
    /// When <paramref name="modelId"/> is null or whitespace, <see cref="IPriceDirectionPredictionEngineRegistry.DefaultModelId"/> is used.
    /// Persists a row when enough candles exist; <see cref="PriceDirectionPredictionEnvelope.StoredId"/> is null otherwise.
    /// LightGBM triple-barrier runs go to <see cref="Domain.Entities.MlLightGbmTripleBarrierPrediction"/>; other engines use <see cref="Domain.Entities.MlPriceDirectionPrediction"/>.
    /// </summary>
    Task<PriceDirectionPredictionEnvelope> PredictForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        string? source = null,
        string? modelId = null,
        CancellationToken ct = default);

    /// <summary>
    /// True when a pending row exists for this ref bar and registered engine (<see cref="PriceDirectionModelIds.MlNetLightGbmTripleBarrierV1"/> uses the LightGBM table; others classic).
    /// </summary>
    Task<bool> HasPendingForEngineAndRefBarAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        string engineModelId,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlPriceDirectionPredictionItemDto>> ListPredictionHistoryAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        int take,
        CancellationToken ct = default);

    /// <summary>
    /// LightGBM triple-barrier engine predictions only (<see cref="PriceDirectionModelIds.MlNetLightGbmTripleBarrierV1"/> persistence).
    /// </summary>
    Task<IReadOnlyList<MlPriceDirectionPredictionItemDto>> ListLightGbmTripleBarrierHistoryAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        int take,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlAutomationPredictionListItemDto>> ListAutomationRecentAsync(
        Guid userId,
        int take,
        CancellationToken ct = default);

    Task ResolvePredictionAsync(
        Guid userId,
        Guid predictionId,
        DateTimeOffset nextBarTime,
        decimal nextClose,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a pending prediction using an already-loaded ascending candle stream (same path as automation — fills N-bar labels when available).
    /// </summary>
    Task ResolvePredictionFromCandlesAsync(
        Guid userId,
        Guid predictionId,
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc,
        string interval,
        CancellationToken ct = default);
}
