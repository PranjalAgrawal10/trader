using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Prediction;

/// <summary>
/// Multi-horizon (1m/5m/15m inferred from bar spacing) LightGBM engine tuned for F&amp;O helper usage.
/// Trains on the latest session window and abstains to neutral under weak confidence.
/// </summary>
public sealed class MlNetFnoMultiHorizonPredictionEngine : IPriceDirectionPredictionEngine
{
    public const string EngineModelId = "mlnet-fno-multi-horizon-v1";

    private const int DefaultMinTrainingRows = 64;
    private const int DefaultConfidenceFloor = 56;
    private readonly ILogger<MlNetFnoMultiHorizonPredictionEngine> _logger;
    private readonly IOptionsMonitor<PriceDirectionPredictionOptions> _opts;
    private readonly IPriceDirectionScoreCalibrator _calibrator;
    private readonly MLContext _ml = new(seed: 43);

    public MlNetFnoMultiHorizonPredictionEngine(
        ILogger<MlNetFnoMultiHorizonPredictionEngine> logger,
        IOptionsMonitor<PriceDirectionPredictionOptions> opts,
        IPriceDirectionScoreCalibrator calibrator)
    {
        _logger = logger;
        _opts = opts;
        _calibrator = calibrator;
    }

    public string ModelId => EngineModelId;

    public string Description =>
        "ML.NET LightGBM multi-horizon F&O classifier (1m/5m/15m profile-aware features with confidence abstain).";

    public PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles)
    {
        if (candles.Count < PriceDirectionPredictionService.MinCandlesRequired)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Neutral,
                0,
                ModelId,
                "Not enough candles for F&O multi-horizon model.");
        }

        var profile = FnoMultiHorizonFeaturePipeline.DetectIntervalProfileKey(candles);
        var opts = _opts.CurrentValue;
        var threshold = ResolveThreshold(opts, profile);
        var minRows = ResolveMinTrainingRows(opts, profile);
        var confidenceFloor = ResolveConfidenceFloor(opts, profile);

        try
        {
            if (!FnoMultiHorizonFeaturePipeline.TryBuildTrainingSet(candles, threshold, out var rows) || rows.Count < minRows)
                return HeuristicFallback(candles, profile, reason: $"insufficient_training_rows<{minRows}>");

            if (!FnoMultiHorizonFeaturePipeline.TryExtractFeatures(candles, candles.Count - 1, out var latest))
                return HeuristicFallback(candles, profile, reason: "feature_extraction_failed");

            var train = rows.Select(static r => new TrainRow { Features = r.Features, Label = r.Label }).ToList();
            var data = _ml.Data.LoadFromEnumerable(train);
            var pipeline = _ml.Transforms.NormalizeMinMax(nameof(TrainRow.Features))
                .Append(_ml.BinaryClassification.Trainers.LightGbm(
                    new LightGbmBinaryTrainer.Options
                    {
                        LabelColumnName = nameof(TrainRow.Label),
                        FeatureColumnName = nameof(TrainRow.Features),
                        NumberOfLeaves = 63,
                        NumberOfIterations = 120,
                        LearningRate = 0.09,
                        MinimumExampleCountPerLeaf = 6,
                        UseCategoricalSplit = false,
                        HandleMissingValue = true,
                    }));

            var model = pipeline.Fit(data);
            var predEngine = _ml.Model.CreatePredictionEngine<InferRow, InferOutput>(model);
            var raw = predEngine.Predict(new InferRow { Features = latest });
            var calibrated = _calibrator.CalibratePUp(raw.Probability, profile);
            var confidence = (int)Math.Clamp(Math.Round(Math.Max(calibrated, 1f - calibrated) * 100), 0, 100);
            if (confidence < confidenceFloor)
            {
                return new PriceDirectionResult(
                    PriceDirectionLabel.Neutral,
                    confidence,
                    ModelId,
                    $"[{profile}] confidence<{confidenceFloor}; abstain to neutral.");
            }

            var isUp = calibrated >= 0.5f;
            return new PriceDirectionResult(
                isUp ? PriceDirectionLabel.Up : PriceDirectionLabel.Down,
                confidence,
                ModelId,
                $"[{profile}] calibrated_pUp={calibrated:0.000}; threshold={threshold:0.######}; rows={rows.Count}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "F&O multi-horizon model failed for profile {Profile}; fallback used.", profile);
            return HeuristicFallback(candles, profile, reason: "exception");
        }
    }

    private static int ResolveMinTrainingRows(PriceDirectionPredictionOptions opts, string profile)
    {
        if (opts.MinTrainingRowsByInterval.TryGetValue(profile, out var custom))
            return Math.Clamp(custom, 24, 5000);
        return DefaultMinTrainingRows;
    }

    private static int ResolveConfidenceFloor(PriceDirectionPredictionOptions opts, string profile)
    {
        if (opts.NeutralConfidenceFloorByInterval.TryGetValue(profile, out var custom))
            return Math.Clamp(custom, 50, 95);
        return DefaultConfidenceFloor;
    }

    private static decimal ResolveThreshold(PriceDirectionPredictionOptions opts, string profile)
    {
        if (opts.LabelThresholdFractionByInterval.TryGetValue(profile, out var custom))
            return Math.Max(0m, custom);
        return Math.Max(0m, opts.LabelThresholdFraction);
    }

    private static PriceDirectionResult HeuristicFallback(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        string profile,
        string reason)
    {
        var prev = candles[^2].Close;
        var last = candles[^1].Close;
        if (last > prev)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Up,
                55,
                "heuristic-momentum",
                $"[{profile}] fallback:{reason}; momentum_up.");
        }

        if (last < prev)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Down,
                55,
                "heuristic-momentum",
                $"[{profile}] fallback:{reason}; momentum_down.");
        }

        return new PriceDirectionResult(
            PriceDirectionLabel.Neutral,
            50,
            "heuristic-momentum",
            $"[{profile}] fallback:{reason}; momentum_flat.");
    }

    private sealed class TrainRow
    {
        [VectorType(FnoMultiHorizonFeaturePipeline.FeatureCount)]
        public float[] Features { get; set; } = Array.Empty<float>();

        public bool Label { get; set; }
    }

    private sealed class InferRow
    {
        [VectorType(FnoMultiHorizonFeaturePipeline.FeatureCount)]
        public float[] Features { get; set; } = Array.Empty<float>();
    }

    private sealed class InferOutput
    {
        [ColumnName("Probability")]
        public float Probability { get; set; }
    }
}

