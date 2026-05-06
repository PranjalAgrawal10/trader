using Trader.Application.Broker;

namespace Trader.Application.Prediction;

public sealed class PriceDirectionPredictionService : IPriceDirectionPredictionService
{
    /// <summary>Minimum bars before calling Kite + ML (depends on default historical window per interval).</summary>
    public const int MinCandlesRequired = 48;

    private readonly IBrokerService _broker;
    private readonly IPriceDirectionPredictionEngine _engine;

    public PriceDirectionPredictionService(IBrokerService broker, IPriceDirectionPredictionEngine engine)
    {
        _broker = broker;
        _engine = engine;
    }

    public async Task<PriceDirectionResult> PredictForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        CancellationToken ct = default)
    {
        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(userId, instrumentToken, interval, fromUtc: null, toUtc: null, ct)
            .ConfigureAwait(false);

        if (hist.Candles.Count < MinCandlesRequired)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Neutral,
                0,
                "insufficient-data",
                $"Need at least {MinCandlesRequired} candles; got {hist.Candles.Count}.");
        }

        var closes = hist.Candles.Select(c => c.Close).ToList();
        return _engine.PredictNextDirection(closes);
    }
}
