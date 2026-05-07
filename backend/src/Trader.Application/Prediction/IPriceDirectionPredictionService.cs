namespace Trader.Application.Prediction;

public interface IPriceDirectionPredictionService
{
    /// <summary>
    /// Loads historical candles via Kite for the authenticated user, then runs <see cref="IPriceDirectionPredictionEngine"/>.
    /// Persists a row when enough candles exist; <see cref="PriceDirectionPredictionEnvelope.StoredId"/> is null otherwise.
    /// </summary>
    Task<PriceDirectionPredictionEnvelope> PredictForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        string? source = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<MlPriceDirectionPredictionItemDto>> ListPredictionHistoryAsync(
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
}
