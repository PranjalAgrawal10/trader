using Trader.Application.Abstractions.Persistence;
using Trader.Application.Broker;
using Trader.Domain.Entities;

namespace Trader.Application.Prediction;

public sealed class PriceDirectionPredictionService : IPriceDirectionPredictionService
{
    /// <summary>Minimum bars before calling Kite + ML (depends on default historical window per interval).</summary>
    public const int MinCandlesRequired = 48;

    public const int MaxStoredPredictionsPerUser = 25_000;

    public const int MaxHistoryTake = 2000;

    public const int MaxAutomationHistoryTake = 500;

    /// <summary>Value stored in <see cref="MlPriceDirectionPrediction.Source"/> for background favorite runs.</summary>
    public const string SourceAutomation = "automation";

    private readonly IBrokerService _broker;
    private readonly IPriceDirectionPredictionEngine _engine;
    private readonly IMlPriceDirectionPredictionRepository _predictions;

    public PriceDirectionPredictionService(
      IBrokerService broker,
      IPriceDirectionPredictionEngine engine,
      IMlPriceDirectionPredictionRepository predictions)
    {
        _broker = broker;
        _engine = engine;
        _predictions = predictions;
    }

    public async Task<PriceDirectionPredictionEnvelope> PredictForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        string? source = null,
        CancellationToken ct = default)
    {
        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(userId, instrumentToken, interval, fromUtc: null, toUtc: null, ct)
            .ConfigureAwait(false);

        if (hist.Candles.Count < MinCandlesRequired)
        {
            var insufficient = new PriceDirectionResult(
                PriceDirectionLabel.Neutral,
                0,
                "insufficient-data",
                $"Need at least {MinCandlesRequired} candles; got {hist.Candles.Count}.");
            return new PriceDirectionPredictionEnvelope(insufficient, null, null, null, null);
        }

        var last = hist.Candles[^1];
        var closes = hist.Candles.Select(c => c.Close).ToList();
        var result = _engine.PredictNextDirection(closes);

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var src = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        if (src is { Length: > 32 })
            src = src[..32];
        var entity = new MlPriceDirectionPrediction
        {
            Id = id,
            UserId = userId,
            InstrumentToken = instrumentToken.Trim(),
            Interval = hist.Interval,
            PredictedAtUtc = now,
            RefBarTimeUtc = last.Time,
            RefClose = last.Close,
            Direction = MapDirectionLabel(result.Direction),
            Confidence = result.Confidence,
            ModelId = result.ModelId,
            Detail = result.Detail,
            Outcome = "pending",
            Source = src,
        };

        await _predictions.AddAsync(entity, ct).ConfigureAwait(false);
        await _predictions.SaveChangesAsync(ct).ConfigureAwait(false);
        await _predictions.PruneForUserAsync(userId, MaxStoredPredictionsPerUser, ct).ConfigureAwait(false);

        return new PriceDirectionPredictionEnvelope(result, id, last.Time, last.Close, now);
    }

    public async Task<IReadOnlyList<MlPriceDirectionPredictionItemDto>> ListPredictionHistoryAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxHistoryTake);
        var token = instrumentToken.Trim();
        var intervalNorm = NormalizeInterval(interval);
        var rows = await _predictions
            .ListForInstrumentAsync(userId, token, intervalNorm, take, ct)
            .ConfigureAwait(false);
        return rows.Select(MapRow).ToList();
    }

    public Task<IReadOnlyList<MlAutomationPredictionListItemDto>> ListAutomationRecentAsync(
        Guid userId,
        int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxAutomationHistoryTake);
        return _predictions.ListAutomationRecentAsync(userId, take, ct);
    }

    public async Task ResolvePredictionAsync(
        Guid userId,
        Guid predictionId,
        DateTimeOffset nextBarTime,
        decimal nextClose,
        CancellationToken ct = default)
    {
        var e = await _predictions.FindTrackedAsync(userId, predictionId, ct).ConfigureAwait(false);
        if (e is null)
            throw new InvalidOperationException("Prediction not found.");
        if (!string.Equals(e.Outcome, "pending", StringComparison.Ordinal))
            throw new InvalidOperationException("Prediction already resolved.");
        if (nextBarTime <= e.RefBarTimeUtc)
            throw new InvalidOperationException("Next bar time must be after the reference bar.");

        e.Outcome = PriceDirectionOutcomeResolver.Resolve(e.Direction, e.RefClose, nextClose);
        e.NextBarTimeUtc = nextBarTime;
        e.NextClose = nextClose;
        await _predictions.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static MlPriceDirectionPredictionItemDto MapRow(MlPriceDirectionPrediction x) =>
        new(
            x.Id,
            x.PredictedAtUtc,
            x.RefBarTimeUtc,
            x.RefClose,
            x.Direction,
            x.Confidence,
            x.ModelId,
            x.Detail,
            x.Outcome,
            x.NextBarTimeUtc,
            x.NextClose,
            x.Source);

    private static string MapDirectionLabel(PriceDirectionLabel d) =>
        d switch
        {
            PriceDirectionLabel.Up => "up",
            PriceDirectionLabel.Down => "down",
            _ => "neutral",
        };

    private static string NormalizeInterval(string interval)
    {
        var t = interval.Trim().ToLowerInvariant();
        return t switch
        {
            "1m" or "2m" or "3m" or "4m" or "5m" or "10m" or "15m" or "30m" or "1h" or "1d" => t,
            _ => throw new InvalidOperationException(
                "Interval must be one of: 1m, 2m, 3m, 4m, 5m, 10m, 15m, 30m, 1h, 1d."),
        };
    }
}
