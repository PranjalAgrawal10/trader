using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Domain.Entities;

namespace Trader.Application.Prediction;

public sealed class PriceDirectionPredictionService : IPriceDirectionPredictionService
{
    /// <summary>Minimum bars before calling Kite + ML (depends on default historical window per interval).</summary>
    public const int MinCandlesRequired = 48;

    public const int MaxStoredPredictionsPerUser = 25_000;

    public const int MaxHistoryTake = 2000;

    public const int MaxAutomationHistoryTake = 5000;

    /// <summary>Value stored on prediction rows for background favorite runs.</summary>
    public const string SourceAutomation = "automation";

    private readonly IBrokerService _broker;
    private readonly IPriceDirectionPredictionEngineRegistry _engines;
    private readonly IMlPriceDirectionPredictionRepository _predictions;
    private readonly IMlLightGbmTripleBarrierPredictionRepository _lightGbmPredictions;
    private readonly IOptionsSnapshot<PriceDirectionPredictionOptions> _opts;

    public PriceDirectionPredictionService(
        IBrokerService broker,
        IPriceDirectionPredictionEngineRegistry engines,
        IMlPriceDirectionPredictionRepository predictions,
        IMlLightGbmTripleBarrierPredictionRepository lightGbmPredictions,
        IOptionsSnapshot<PriceDirectionPredictionOptions> opts)
    {
        _broker = broker;
        _engines = engines;
        _predictions = predictions;
        _lightGbmPredictions = lightGbmPredictions;
        _opts = opts;
    }

    public async Task<PriceDirectionPredictionEnvelope> PredictForInstrumentAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        string? source = null,
        string? modelId = null,
        bool bestOfThreeSlidingWindow = false,
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
        PriceDirectionResult result;
        if (bestOfThreeSlidingWindow &&
            PriceDirectionBestOfThree.TryCompute(hist.Candles, engine, MinCandlesRequired, out var merged, out var b3Prefix))
        {
            result = merged with { Detail = b3Prefix + merged.Detail };
        }
        else
        {
            result = engine.PredictNextDirection(hist.Candles);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var src = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        if (src is { Length: > 32 })
            src = src[..32];

        var engineModelId = engine.ModelId;
        var persistLightGbm = string.Equals(
            engineModelId,
            PriceDirectionModelIds.MlNetLightGbmTripleBarrierV1,
            StringComparison.OrdinalIgnoreCase);

        var appliedThreshold = Math.Max(0m, _opts.Value.LabelThresholdFraction);

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
                EngineModelId = engineModelId,
                ModelId = result.ModelId,
                Detail = result.Detail,
                Outcome = "pending",
                Source = src,
                LabelThresholdFractionApplied = appliedThreshold,
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
                EngineModelId = engineModelId,
                ModelId = result.ModelId,
                Detail = result.Detail,
                Outcome = "pending",
                Source = src,
                LabelThresholdFractionApplied = appliedThreshold,
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

    public async Task<bool> HasPendingForEngineAndRefBarAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset refBarTimeUtc,
        string engineModelId,
        CancellationToken ct = default)
    {
        var token = instrumentToken.Trim();
        var intervalNorm = NormalizeInterval(interval);
        var eid = engineModelId.Trim();

        if (string.Equals(eid, PriceDirectionModelIds.MlNetLightGbmTripleBarrierV1, StringComparison.OrdinalIgnoreCase))
        {
            return await _lightGbmPredictions
                .HasPendingForRefBarAndEngineModelAsync(userId, token, intervalNorm, refBarTimeUtc, eid, ct)
                .ConfigureAwait(false);
        }

        return await _predictions
            .HasPendingForRefBarAndEngineModelAsync(userId, token, intervalNorm, refBarTimeUtc, eid, ct)
            .ConfigureAwait(false);
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
        var fetchEach = Math.Min(Math.Max(take * 25, take), MaxAutomationHistoryTake);
        var legacy = await _predictions.ListAutomationRecentAsync(userId, fetchEach, ct).ConfigureAwait(false);
        var gbm = await _lightGbmPredictions.ListAutomationRecentAsync(userId, fetchEach, ct).ConfigureAwait(false);
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
            await ResolveClassicWithBrokerVerifyAsync(userId, legacy, nextBarTime, nextClose, ct).ConfigureAwait(false);
            return;
        }

        var gbmRow = await _lightGbmPredictions.FindTrackedAsync(userId, predictionId, ct).ConfigureAwait(false);
        if (gbmRow is not null)
        {
            await ResolveGbmWithBrokerVerifyAsync(userId, gbmRow, nextBarTime, nextClose, ct).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Prediction not found.");
    }

    public async Task ResolvePredictionFromCandlesAsync(
        Guid userId,
        Guid predictionId,
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc,
        string interval,
        CancellationToken ct = default)
    {
        var intervalNorm = NormalizeInterval(interval);

        var legacy = await _predictions.FindTrackedAsync(userId, predictionId, ct).ConfigureAwait(false);
        if (legacy is not null)
        {
            if (!string.Equals(legacy.Outcome, "pending", StringComparison.Ordinal))
                throw new InvalidOperationException("Prediction already resolved.");

            var thresh = legacy.LabelThresholdFractionApplied ?? Math.Max(0m, _opts.Value.LabelThresholdFraction);
            ApplyResolutionSnapshot(legacy, candlesAsc, intervalNorm, thresh, null);
            await _predictions.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var gbmRow = await _lightGbmPredictions.FindTrackedAsync(userId, predictionId, ct).ConfigureAwait(false);
        if (gbmRow is not null)
        {
            if (!string.Equals(gbmRow.Outcome, "pending", StringComparison.Ordinal))
                throw new InvalidOperationException("Prediction already resolved.");

            var thresh = gbmRow.LabelThresholdFractionApplied ?? Math.Max(0m, _opts.Value.LabelThresholdFraction);
            ApplyResolutionSnapshot(gbmRow, candlesAsc, intervalNorm, thresh, null);
            await _lightGbmPredictions.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("Prediction not found.");
    }

    private async Task ResolveClassicWithBrokerVerifyAsync(
        Guid userId,
        MlPriceDirectionPrediction legacy,
        DateTimeOffset nextBarTime,
        decimal nextClose,
        CancellationToken ct)
    {
        if (!string.Equals(legacy.Outcome, "pending", StringComparison.Ordinal))
            throw new InvalidOperationException("Prediction already resolved.");
        if (nextBarTime <= legacy.RefBarTimeUtc)
            throw new InvalidOperationException("Next bar time must be after the reference bar.");

        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(
                userId,
                legacy.InstrumentToken,
                legacy.Interval,
                fromUtc: null,
                toUtc: null,
                ct)
            .ConfigureAwait(false);

        var thresh = legacy.LabelThresholdFractionApplied ?? Math.Max(0m, _opts.Value.LabelThresholdFraction);
        ApplyResolutionSnapshot(legacy, hist.Candles, hist.Interval, thresh, new Verification(nextBarTime, nextClose));
        await _predictions.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task ResolveGbmWithBrokerVerifyAsync(
        Guid userId,
        MlLightGbmTripleBarrierPrediction gbmRow,
        DateTimeOffset nextBarTime,
        decimal nextClose,
        CancellationToken ct)
    {
        if (!string.Equals(gbmRow.Outcome, "pending", StringComparison.Ordinal))
            throw new InvalidOperationException("Prediction already resolved.");
        if (nextBarTime <= gbmRow.RefBarTimeUtc)
            throw new InvalidOperationException("Next bar time must be after the reference bar.");

        var hist = await _broker
            .GetKiteHistoricalCandlesAsync(
                userId,
                gbmRow.InstrumentToken,
                gbmRow.Interval,
                fromUtc: null,
                toUtc: null,
                ct)
            .ConfigureAwait(false);

        var thresh = gbmRow.LabelThresholdFractionApplied ?? Math.Max(0m, _opts.Value.LabelThresholdFraction);
        ApplyResolutionSnapshot(gbmRow, hist.Candles, hist.Interval, thresh, new Verification(nextBarTime, nextClose));
        await _lightGbmPredictions.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private readonly record struct Verification(DateTimeOffset Time, decimal Close);

    private static void ApplyResolutionSnapshot(
        MlPriceDirectionPrediction row,
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc,
        string intervalNorm,
        decimal labelThresholdFraction,
        Verification? verify)
    {
        var snap = ComputeResolutionSnapshot(
            row.Direction,
            row.RefClose,
            row.RefBarTimeUtc,
            candlesAsc,
            intervalNorm,
            labelThresholdFraction,
            verify);
        CopySnapshotTo(row, snap);
    }

    private static void ApplyResolutionSnapshot(
        MlLightGbmTripleBarrierPrediction row,
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc,
        string intervalNorm,
        decimal labelThresholdFraction,
        Verification? verify)
    {
        var snap = ComputeResolutionSnapshot(
            row.Direction,
            row.RefClose,
            row.RefBarTimeUtc,
            candlesAsc,
            intervalNorm,
            labelThresholdFraction,
            verify);
        CopySnapshotTo(row, snap);
    }

    private readonly record struct ResolutionSnapshot(
        string Outcome,
        DateTimeOffset NextBarTimeUtc,
        decimal NextClose,
        sbyte? LabelNextBar,
        string? CensorReason,
        DateTimeOffset? NextBarTimeUtcN3,
        decimal? NextCloseN3,
        sbyte? LabelN3,
        DateTimeOffset? NextBarTimeUtcN5,
        decimal? NextCloseN5,
        sbyte? LabelN5);

    private static ResolutionSnapshot ComputeResolutionSnapshot(
        string direction,
        decimal refClose,
        DateTimeOffset refBarTimeUtc,
        IReadOnlyList<KiteHistoricalCandlePointDto> candlesAsc,
        string intervalNorm,
        decimal labelThresholdFraction,
        Verification? verify)
    {
        var idx = MlResolutionCandleLocator.FindRefBarIndex(candlesAsc, refBarTimeUtc);
        if (idx < 0 || idx + 1 >= candlesAsc.Count)
            throw new InvalidOperationException("Historical candles missing the reference or next bar; cannot resolve.");

        var nominal = MlChartIntervalDuration.FromNormalizedInterval(intervalNorm);
        var next = candlesAsc[idx + 1];
        VerifyNextMatchesKite(next, verify, refClose);

        var censorReason = MlResolutionCandleLocator.LooksLikeSessionGap(nominal, candlesAsc[idx].Time, next.Time)
            ? "gap_too_large"
            : null;

        var thresh = Math.Max(0m, labelThresholdFraction);
        var outcome = PriceDirectionOutcomeResolver.Resolve(direction, refClose, next.Close, thresh);
        var labelNext = PriceDirectionLabeling.ClassifySignedLabel(refClose, next.Close, thresh);

        DateTimeOffset? n3t = null;
        decimal? n3c = null;
        sbyte? l3 = null;
        if (idx + 3 < candlesAsc.Count)
        {
            var c3 = candlesAsc[idx + 3];
            n3t = c3.Time;
            n3c = c3.Close;
            l3 = PriceDirectionLabeling.ClassifySignedLabel(refClose, c3.Close, thresh);
        }

        DateTimeOffset? n5t = null;
        decimal? n5c = null;
        sbyte? l5 = null;
        if (idx + 5 < candlesAsc.Count)
        {
            var c5 = candlesAsc[idx + 5];
            n5t = c5.Time;
            n5c = c5.Close;
            l5 = PriceDirectionLabeling.ClassifySignedLabel(refClose, c5.Close, thresh);
        }

        return new ResolutionSnapshot(outcome, next.Time, next.Close, labelNext, censorReason, n3t, n3c, l3, n5t, n5c, l5);
    }

    private static void CopySnapshotTo(MlPriceDirectionPrediction row, ResolutionSnapshot s)
    {
        row.Outcome = s.Outcome;
        row.NextBarTimeUtc = s.NextBarTimeUtc;
        row.NextClose = s.NextClose;
        row.LabelNextBar = s.LabelNextBar;
        row.CensorReason = s.CensorReason;
        row.NextBarTimeUtcN3 = s.NextBarTimeUtcN3;
        row.NextCloseN3 = s.NextCloseN3;
        row.LabelN3 = s.LabelN3;
        row.NextBarTimeUtcN5 = s.NextBarTimeUtcN5;
        row.NextCloseN5 = s.NextCloseN5;
        row.LabelN5 = s.LabelN5;
    }

    private static void CopySnapshotTo(MlLightGbmTripleBarrierPrediction row, ResolutionSnapshot s)
    {
        row.Outcome = s.Outcome;
        row.NextBarTimeUtc = s.NextBarTimeUtc;
        row.NextClose = s.NextClose;
        row.LabelNextBar = s.LabelNextBar;
        row.CensorReason = s.CensorReason;
        row.NextBarTimeUtcN3 = s.NextBarTimeUtcN3;
        row.NextCloseN3 = s.NextCloseN3;
        row.LabelN3 = s.LabelN3;
        row.NextBarTimeUtcN5 = s.NextBarTimeUtcN5;
        row.NextCloseN5 = s.NextCloseN5;
        row.LabelN5 = s.LabelN5;
    }

    private static void VerifyNextMatchesKite(
        KiteHistoricalCandlePointDto nextFromHist,
        Verification? verify,
        decimal refClose)
    {
        if (verify is null)
            return;

        var v = verify.Value;
        if (Math.Abs((nextFromHist.Time - v.Time).TotalSeconds) > 120)
            throw new InvalidOperationException("Next candle from Kite does not match PATCH nextBarTime.");

        var tol = Math.Max(Math.Abs(refClose) * 0.00001m, 0.005m);
        if (Math.Abs(nextFromHist.Close - v.Close) > tol)
            throw new InvalidOperationException("Next candle close from Kite does not match PATCH nextClose.");
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
            x.Source,
            x.EngineModelId,
            x.LabelThresholdFractionApplied,
            x.CensorReason,
            x.LabelNextBar,
            x.LabelN3,
            x.LabelN5,
            x.NextBarTimeUtcN3,
            x.NextCloseN3,
            x.NextBarTimeUtcN5,
            x.NextCloseN5);

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
            x.Source,
            x.EngineModelId,
            x.LabelThresholdFractionApplied,
            x.CensorReason,
            x.LabelNextBar,
            x.LabelN3,
            x.LabelN5,
            x.NextBarTimeUtcN3,
            x.NextCloseN3,
            x.NextBarTimeUtcN5,
            x.NextCloseN5);

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
