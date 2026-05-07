using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.Extensions.Logging;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Prediction;

/// <summary>
/// On-the-fly <see cref="SdcaLogisticRegression"/> over short-term return / SMA-gap features.
/// Not investment advice; demonstrates the ML layer. Falls back to a simple momentum heuristic on failure.
/// </summary>
public sealed class MlNetPriceDirectionPredictionEngine : IPriceDirectionPredictionEngine
{
    public const string EngineModelId = "mlnet-sdca-logistic-v1";

    public string ModelId => EngineModelId;

    public string Description => "ML.NET on-the-fly SDCA logistic regression (return / SMA-gap features).";

    private const int FirstFeatureIndex = 14;
    private readonly ILogger<MlNetPriceDirectionPredictionEngine> _logger;
    private readonly MLContext _ml;

    public MlNetPriceDirectionPredictionEngine(ILogger<MlNetPriceDirectionPredictionEngine> logger)
    {
        _logger = logger;
        _ml = new MLContext(seed: 42);
    }

    public PriceDirectionResult PredictNextDirection(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < PriceDirectionPredictionService.MinCandlesRequired)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Neutral,
                0,
                ModelId,
                "Not enough closes for ML.");
        }

        try
        {
            var rows = BuildTrainingRows(closes);
            if (rows.Count < 24)
                return Heuristic(closes);

            var data = _ml.Data.LoadFromEnumerable(rows);
            var pipeline = _ml.Transforms
                .Concatenate(
                    "Features",
                    nameof(DirectionExample.F1),
                    nameof(DirectionExample.F2),
                    nameof(DirectionExample.F3),
                    nameof(DirectionExample.F4),
                    nameof(DirectionExample.F5),
                    nameof(DirectionExample.F6))
                .Append(_ml.BinaryClassification.Trainers.SdcaLogisticRegression(
                    labelColumnName: nameof(DirectionExample.Label),
                    featureColumnName: "Features",
                    maximumNumberOfIterations: 80));

            var model = pipeline.Fit(data);
            var engine = _ml.Model.CreatePredictionEngine<DirectionFeatures, DirectionProbOutput>(model);
            var feat = ExtractFeatures(closes, closes.Count - 1);
            if (feat is null)
                return Heuristic(closes);

            var pred = engine.Predict(feat);
            var pUp = pred.Probability;
            var confidence = (int)Math.Clamp(Math.Round(Math.Max(pUp, 1f - pUp) * 100), 0, 100);

            if (confidence < 53)
            {
                return new PriceDirectionResult(
                    PriceDirectionLabel.Neutral,
                    confidence,
                    ModelId,
                    "Model output near 50% — no strong directional bias.");
            }

            return pred.Prediction
                ? new PriceDirectionResult(
                    PriceDirectionLabel.Up,
                    confidence,
                    ModelId,
                    "Classifier favors a higher next close vs current (historical pattern fit).")
                : new PriceDirectionResult(
                    PriceDirectionLabel.Down,
                    confidence,
                    ModelId,
                    "Classifier favors a lower next close vs current (historical pattern fit).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ML.NET price-direction pipeline failed; heuristic fallback.");
            return Heuristic(closes);
        }
    }

    private static PriceDirectionResult Heuristic(IReadOnlyList<decimal> closes)
    {
        var a = closes[^2];
        var b = closes[^1];
        if (b > a)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Up,
                55,
                "heuristic-momentum",
                "Fallback: simple momentum (last close vs prior).");
        }

        if (b < a)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Down,
                55,
                "heuristic-momentum",
                "Fallback: simple momentum (last close vs prior).");
        }

        return new PriceDirectionResult(
            PriceDirectionLabel.Neutral,
            50,
            "heuristic-momentum",
            "Fallback: flat last bar.");
    }

    private static List<DirectionExample> BuildTrainingRows(IReadOnlyList<decimal> closes)
    {
        var list = new List<DirectionExample>();
        for (var i = FirstFeatureIndex; i < closes.Count - 1; i++)
        {
            var f = ExtractFeatures(closes, i);
            if (f is null)
                continue;
            list.Add(new DirectionExample
            {
                F1 = f.F1,
                F2 = f.F2,
                F3 = f.F3,
                F4 = f.F4,
                F5 = f.F5,
                F6 = f.F6,
                Label = closes[i + 1] > closes[i],
            });
        }

        return list;
    }

    private static DirectionFeatures? ExtractFeatures(IReadOnlyList<decimal> c, int i)
    {
        if (i < FirstFeatureIndex || i >= c.Count)
            return null;

        static float Ret(IReadOnlyList<decimal> x, int idx, int lag)
        {
            if (idx - lag < 0)
                return 0f;
            var prev = x[idx - lag];
            if (prev == 0)
                return 0f;
            return (float)((x[idx] - prev) / prev);
        }

        static decimal Sma(IReadOnlyList<decimal> x, int idx, int period)
        {
            decimal s = 0;
            for (var k = 0; k < period; k++)
                s += x[idx - k];
            return s / period;
        }

        var sma5 = Sma(c, i, 5);
        var sma10 = Sma(c, i, 10);
        var f6 = sma10 == 0 ? 0f : (float)((sma5 - sma10) / sma10);

        return new DirectionFeatures
        {
            F1 = Ret(c, i, 1),
            F2 = Ret(c, i, 2),
            F3 = Ret(c, i, 3),
            F4 = Ret(c, i, 5),
            F5 = Ret(c, i, 10),
            F6 = f6,
        };
    }

    private sealed class DirectionExample
    {
        public float F1 { get; set; }
        public float F2 { get; set; }
        public float F3 { get; set; }
        public float F4 { get; set; }
        public float F5 { get; set; }
        public float F6 { get; set; }
        public bool Label { get; set; }
    }

    public sealed class DirectionFeatures
    {
        public float F1 { get; set; }
        public float F2 { get; set; }
        public float F3 { get; set; }
        public float F4 { get; set; }
        public float F5 { get; set; }
        public float F6 { get; set; }
    }

    private sealed class DirectionProbOutput
    {
        [ColumnName("PredictedLabel")]
        public bool Prediction { get; set; }

        /// <summary>Probability of the positive class (Label = true = next close up).</summary>
        [ColumnName("Probability")]
        public float Probability { get; set; }
    }
}
