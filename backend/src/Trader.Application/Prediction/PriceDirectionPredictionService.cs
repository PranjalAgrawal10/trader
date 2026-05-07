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



    /// <summary>Value stored on prediction rows for background favorite runs.</summary>

    public const string SourceAutomation = "automation";



    private readonly IBrokerService _broker;

    private readonly IPriceDirectionPredictionEngineRegistry _engines;

    private readonly IMlPriceDirectionPredictionRepository _predictions;

    private readonly IMlLightGbmTripleBarrierPredictionRepository _lightGbmPredictions;



    public PriceDirectionPredictionService(

        IBrokerService broker,

        IPriceDirectionPredictionEngineRegistry engines,

        IMlPriceDirectionPredictionRepository predictions,

        IMlLightGbmTripleBarrierPredictionRepository lightGbmPredictions)

    {

        _broker = broker;

        _engines = engines;

        _predictions = predictions;

        _lightGbmPredictions = lightGbmPredictions;

    }



    public async Task<PriceDirectionPredictionEnvelope> PredictForInstrumentAsync(

        Guid userId,

        string instrumentToken,

        string interval,

        string? source = null,

        string? modelId = null,

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

            return new PriceDirectionPredictionEnvelope(
                insufficient,
                null,
                null,
                null,
                null,
                MlPredictionPersistenceKind.None);

        }



        var last = hist.Candles[^1];

        var engine = _engines.Resolve(modelId);

        var result = engine.PredictNextDirection(hist.Candles);



        var id = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;

        var src = string.IsNullOrWhiteSpace(source) ? null : source.Trim();

        if (src is { Length: > 32 })

            src = src[..32];



        var persistLightGbm = string.Equals(

            engine.ModelId,

            PriceDirectionModelIds.MlNetLightGbmTripleBarrierV1,

            StringComparison.OrdinalIgnoreCase);



        if (persistLightGbm)

        {

            var gbm = new MlLightGbmTripleBarrierPrediction

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

            await _lightGbmPredictions.AddAsync(gbm, ct).ConfigureAwait(false);

            await _lightGbmPredictions.SaveChangesAsync(ct).ConfigureAwait(false);

            await _lightGbmPredictions.PruneForUserAsync(userId, MaxStoredPredictionsPerUser, ct).ConfigureAwait(false);

        }

        else

        {

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

        }



        return new PriceDirectionPredictionEnvelope(
            result,
            id,
            last.Time,
            last.Close,
            now,
            persistLightGbm
                ? MlPredictionPersistenceKind.LightGbmTripleBarrier
                : MlPredictionPersistenceKind.ClassicPriceDirection);

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



    public async Task<IReadOnlyList<MlPriceDirectionPredictionItemDto>> ListLightGbmTripleBarrierHistoryAsync(

        Guid userId,

        string instrumentToken,

        string interval,

        int take,

        CancellationToken ct = default)

    {

        take = Math.Clamp(take, 1, MaxHistoryTake);

        var token = instrumentToken.Trim();

        var intervalNorm = NormalizeInterval(interval);

        var rows = await _lightGbmPredictions

            .ListForInstrumentAsync(userId, token, intervalNorm, take, ct)

            .ConfigureAwait(false);

        return rows.Select(MapLightGbmRow).ToList();

    }



    public async Task<IReadOnlyList<MlAutomationPredictionListItemDto>> ListAutomationRecentAsync(

        Guid userId,

        int take,

        CancellationToken ct = default)

    {

        take = Math.Clamp(take, 1, MaxAutomationHistoryTake);

        var legacy = await _predictions.ListAutomationRecentAsync(userId, take, ct).ConfigureAwait(false);

        var gbm = await _lightGbmPredictions.ListAutomationRecentAsync(userId, take, ct).ConfigureAwait(false);

        return legacy

            .Concat(gbm)

            .OrderByDescending(x => x.PredictedAt)

            .Take(take)

            .ToList();

    }



    public async Task ResolvePredictionAsync(

        Guid userId,

        Guid predictionId,

        DateTimeOffset nextBarTime,

        decimal nextClose,

        CancellationToken ct = default)

    {

        var legacy = await _predictions.FindTrackedAsync(userId, predictionId, ct).ConfigureAwait(false);

        if (legacy is not null)

        {

            if (!string.Equals(legacy.Outcome, "pending", StringComparison.Ordinal))

                throw new InvalidOperationException("Prediction already resolved.");

            if (nextBarTime <= legacy.RefBarTimeUtc)

                throw new InvalidOperationException("Next bar time must be after the reference bar.");



            legacy.Outcome = PriceDirectionOutcomeResolver.Resolve(legacy.Direction, legacy.RefClose, nextClose);

            legacy.NextBarTimeUtc = nextBarTime;

            legacy.NextClose = nextClose;

            await _predictions.SaveChangesAsync(ct).ConfigureAwait(false);

            return;

        }



        var gbmRow = await _lightGbmPredictions.FindTrackedAsync(userId, predictionId, ct).ConfigureAwait(false);

        if (gbmRow is not null)

        {

            if (!string.Equals(gbmRow.Outcome, "pending", StringComparison.Ordinal))

                throw new InvalidOperationException("Prediction already resolved.");

            if (nextBarTime <= gbmRow.RefBarTimeUtc)

                throw new InvalidOperationException("Next bar time must be after the reference bar.");



            gbmRow.Outcome = PriceDirectionOutcomeResolver.Resolve(gbmRow.Direction, gbmRow.RefClose, nextClose);

            gbmRow.NextBarTimeUtc = nextBarTime;

            gbmRow.NextClose = nextClose;

            await _lightGbmPredictions.SaveChangesAsync(ct).ConfigureAwait(false);

            return;

        }



        throw new InvalidOperationException("Prediction not found.");

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



    private static MlPriceDirectionPredictionItemDto MapLightGbmRow(MlLightGbmTripleBarrierPrediction x) =>

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


