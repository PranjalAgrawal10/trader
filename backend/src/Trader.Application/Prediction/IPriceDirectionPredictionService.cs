namespace Trader.Application.Prediction;

public interface IPriceDirectionPredictionService
{
    /// <summary>
    /// Loads historical candles via Kite for the authenticated user, then runs <see cref="IPriceDirectionPredictionEngine"/>.
    /// </summary>
    Task<PriceDirectionResult> PredictForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        CancellationToken ct = default);
}
