using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;
using Trader.Application.Broker;
using Trader.Application.Prediction;

namespace Trader.Infrastructure.Prediction;

/// <summary>
/// On-the-fly binary LightGBM trainer with Lopez-style triple-barrier-inspired labels (<strong>candle-bar</strong>
/// approximation: volatile horizontal barriers plus a vertical max-hold horizon). Consumes OHLCV plus backend overlays
/// (SR, EMA where present). Falls back to a simple momentum heuristic on failure — not investment advice.
/// </summary>
public sealed class MlNetLightGbmTripleBarrierPredictionEngine : IPriceDirectionPredictionEngine
{
    public const string EngineModelId = "mlnet-lightgbm-triple-barrier-v1";

    public string ModelId => EngineModelId;

    public string Description =>
        "ML.NET LightGBM on OHLC/tabular microstructure labels (triple-barrier style on candles; SR/VWAP-derived features where available).";

    private const int VolLookback = 20;

    /// <summary>Horizontal barriers scaled as <c>entry ± k × σ</c> where σ is rolling close-return stdev.</summary>
    private const float KBarrier = 1.5f;

    /// <summary>Vertical barrier unless a horizontal barrier clears first.</summary>
    private const int HorizonBars = 10;

    private const int VwapWindow = VolLookback;
    private const int VolSmaPeriod = 10;
    private const int FirstFeatureIndex = 30;

    /// <summary>Exclude neutral (time-expiry) barriers from fitting the binary learner.</summary>
    private const int MinTrainingRows = 36;

    private readonly ILogger<MlNetLightGbmTripleBarrierPredictionEngine> _logger;
    private readonly MLContext _ml;

    public MlNetLightGbmTripleBarrierPredictionEngine(ILogger<MlNetLightGbmTripleBarrierPredictionEngine> logger)
    {
        _logger = logger;
        _ml = new MLContext(seed: 42);
    }

    public PriceDirectionResult PredictNextDirection(IReadOnlyList<KiteHistoricalCandlePointDto> candles)
    {
        if (candles.Count < PriceDirectionPredictionService.MinCandlesRequired)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Neutral,
                0,
                ModelId,
                "Not enough candles for ML.");
        }

        try
        {
            var rows = BuildTrainingRows(candles);
            if (rows.Count < MinTrainingRows)
                return HeuristicMomentum(candles);

            var feat = ExtractFeatures(candles, candles.Count - 1);
            if (feat is null)
                return HeuristicMomentum(candles);

            var data = _ml.Data.LoadFromEnumerable(rows);
            var pipeline = _ml.Transforms
                .Concatenate(
                    "Features",
                    nameof(TbExample.F1),
                    nameof(TbExample.F2),
                    nameof(TbExample.F3),
                    nameof(TbExample.F4),
                    nameof(TbExample.F5),
                    nameof(TbExample.F6),
                    nameof(TbExample.F7),
                    nameof(TbExample.F8),
                    nameof(TbExample.F9),
                    nameof(TbExample.F10),
                    nameof(TbExample.F11),
                    nameof(TbExample.F12),
                    nameof(TbExample.F13),
                    nameof(TbExample.F14),
                    nameof(TbExample.F15),
                    nameof(TbExample.F16))
                .Append(_ml.BinaryClassification.Trainers.LightGbm(
                    labelColumnName: nameof(TbExample.Label),
                    featureColumnName: "Features",
                    numberOfLeaves: 31,
                    numberOfIterations: 80,
                    minimumExampleCountPerLeaf: 4,
                    learningRate: 0.18));

            var model = pipeline.Fit(data);
            var engine = _ml.Model.CreatePredictionEngine<TripleBarrierFeatures, BarrierProbOutput>(model);

            var pred = engine.Predict(feat);
            var pProfit = pred.Probability;
            var confidence =
                (int)Math.Clamp(Math.Round(Math.Max(pProfit, 1f - pProfit) * 100), 0, 100);

            if (confidence < 54)
            {
                return new PriceDirectionResult(
                    PriceDirectionLabel.Neutral,
                    confidence,
                    ModelId,
                    "Model output near 50% — no strong bias vs triple-barrier target.");
            }

            return pred.Prediction
                ? new PriceDirectionResult(
                    PriceDirectionLabel.Up,
                    confidence,
                    ModelId,
                    "Classifier favors upper barrier resolving before lower (tabular / microstructure pattern fit).")
                : new PriceDirectionResult(
                    PriceDirectionLabel.Down,
                    confidence,
                    ModelId,
                    "Classifier favors lower barrier resolving before upper (tabular / microstructure pattern fit).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LightGBM triple-barrier pipeline failed; heuristic fallback.");
            return HeuristicMomentum(candles);
        }
    }

    private static PriceDirectionResult HeuristicMomentum(IReadOnlyList<KiteHistoricalCandlePointDto> candles)
    {
        var a = candles[^2].Close;
        var b = candles[^1].Close;
        if (b > a)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Up,
                55,
                "heuristic-momentum",
                "Fallback: simple momentum after LightGBM could not score.");
        }

        if (b < a)
        {
            return new PriceDirectionResult(
                PriceDirectionLabel.Down,
                55,
                "heuristic-momentum",
                "Fallback: simple momentum after LightGBM could not score.");
        }

        return new PriceDirectionResult(
            PriceDirectionLabel.Neutral,
            50,
            "heuristic-momentum",
            "Fallback: flat last bar.");
    }

    private static List<TbExample> BuildTrainingRows(IReadOnlyList<KiteHistoricalCandlePointDto> c)
    {
        var list = new List<TbExample>();
        for (var i = FirstFeatureIndex; i <= c.Count - 2; i++)
        {
            if (!TryGetTripleBarrierProfitLabel(c, i, HorizonBars, KBarrier, out var labelProfit))
                continue;

            var f = ExtractFeatures(c, i);
            if (f is null)
                continue;

            list.Add(new TbExample
            {
                F1 = f.F1,
                F2 = f.F2,
                F3 = f.F3,
                F4 = f.F4,
                F5 = f.F5,
                F6 = f.F6,
                F7 = f.F7,
                F8 = f.F8,
                F9 = f.F9,
                F10 = f.F10,
                F11 = f.F11,
                F12 = f.F12,
                F13 = f.F13,
                F14 = f.F14,
                F15 = f.F15,
                F16 = f.F16,
                Label = labelProfit,
            });
        }

        return list;
    }

    /// <summary>
    /// <see langword="true"/> when the profit barrier clears before loss within the horizon; <see langword="false"/>
    /// when loss clears first (including pessimistic simultaneous touch). Neutral time exits return <see langword="false"/>.
    /// </summary>
    private static bool TryGetTripleBarrierProfitLabel(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        int entryIndex,
        int maxHorizonBars,
        float kMultiplier,
        out bool labelProfit)
    {
        labelProfit = default;
        if (entryIndex < VolLookback || entryIndex + 1 >= candles.Count)
            return false;

        var entry = candles[entryIndex].Close;
        if (entry == 0)
            return false;

        var sigma = RollingCloseReturnStdDev(candles, entryIndex, VolLookback);
        if (sigma < 1e-8f || float.IsNaN(sigma))
            return false;

        var bUp = entry * (1 + (decimal)kMultiplier * (decimal)sigma);
        var bDown = entry * (1 - (decimal)kMultiplier * (decimal)sigma);

        var endInclusive = Math.Min(entryIndex + maxHorizonBars, candles.Count - 1);
        for (var j = entryIndex + 1; j <= endInclusive; j++)
        {
            var low = candles[j].Low;
            var high = candles[j].High;

            var hitDown = low <= bDown;
            var hitUp = high >= bUp;

            // If both horizons are crossed in one bar, assume adverse stop binds first (conservative execution).
            if (hitDown && hitUp)
            {
                labelProfit = false;
                return true;
            }

            if (hitDown)
            {
                labelProfit = false;
                return true;
            }

            if (hitUp)
            {
                labelProfit = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>Population-style stdev over the previous <paramref name="lookback"/> closes ending at index <paramref name="i"/>.</summary>
    private static float RollingCloseReturnStdDev(IReadOnlyList<KiteHistoricalCandlePointDto> candles, int iEnd, int lookback)
    {
        if (iEnd < lookback)
            return 0f;

        double sum = 0;
        double sumSq = 0;
        var n = lookback;

        for (var t = iEnd - lookback + 1; t <= iEnd; t++)
        {
            var prev = (double)candles[t - 1].Close;
            var cur = (double)candles[t].Close;
            if (prev <= 0)
                continue;
            var r = (cur - prev) / prev;
            sum += r;
            sumSq += r * r;
        }

        var mean = sum / n;
        var variance = Math.Max(0, sumSq / n - mean * mean);
        return (float)Math.Sqrt(variance);
    }

    private static TripleBarrierFeatures? ExtractFeatures(IReadOnlyList<KiteHistoricalCandlePointDto> c, int i)
    {
        if (i < FirstFeatureIndex || i >= c.Count)
            return null;

        static float Ret(IReadOnlyList<KiteHistoricalCandlePointDto> x, int idx, int lag)
        {
            if (idx - lag < 0)
                return 0f;
            var prev = x[idx - lag].Close;
            var cur = x[idx].Close;
            if (prev == 0)
                return 0f;
            return (float)((cur - prev) / prev);
        }

        static decimal Sma(IReadOnlyList<KiteHistoricalCandlePointDto> x, int idx, int period)
        {
            decimal s = 0;
            for (var k = 0; k < period; k++)
                s += x[idx - k].Close;

            return s / period;
        }

        decimal SmaVol(IReadOnlyList<KiteHistoricalCandlePointDto> x, int idx, int period)
        {
            decimal s = 0;
            for (var k = 0; k < period; k++)
                s += Math.Max(x[idx - k].Volume, 0L);

            return s == 0 ? 0 : s / period;
        }

        var close = c[i].Close;
        var open = c[i].Open;
        var high = c[i].High;
        var low = c[i].Low;
        var vol = Math.Max(c[i].Volume, 0L);

        var sma5 = Sma(c, i, 5);
        var sma10 = Sma(c, i, 10);
        if (close == 0 || sma5 == 0 || sma10 == 0)
            return null;

        var bodyPct =
            close == 0 ? 0f : (float)((double)(close - open) / (double)close);
        var denomRange = high - low;
        if (denomRange == 0)
            denomRange = close != 0 ? Math.Abs(close) * 0.0000001m : 0.00001m;
        var rangePct = close == 0 ? 0f : (float)((double)(high - low) / (double)close);
        var inRange =
            (float)Math.Clamp((double)((close - low) / denomRange), 0d, 1d) - 0.5f;

        var vAvg = SmaVol(c, i, VolSmaPeriod);
        var volRatio =
            vAvg <= 0 ? 1f : (float)((double)Math.Max(vol, 0L) / (double)vAvg);

        var vw = RollingTypicalVwap(c, i, VwapWindow);
        var vwDev = vw <= 0 || close <= 0
            ? 0f
            : (float)((double)((close - vw) / vw));

        float distSup = 0f;
        if (c[i].SrSupport is { } sup && close != 0)
            distSup = (float)((double)((close - sup) / close));

        float distRes = 0f;
        if (c[i].SrResistance is { } res && close != 0)
            distRes = (float)((double)((res - close) / close));

        float ema9gap = 0f;
        if (c[i].Ema9 is { } ema9 && ema9 != 0)
            ema9gap = (float)((double)((close - ema9) / ema9));

        float ema21gap = 0f;
        if (c[i].Ema21 is { } ema21 && ema21 != 0)
            ema21gap = (float)((double)((close - ema21) / ema21));

        var fSmaGap5 = sma5 == 0 ? 0f : (float)((double)((close - sma5) / sma5));
        var fSmaGap10 = sma10 == 0 ? 0f : (float)((double)((close - sma10) / sma10));

        return new TripleBarrierFeatures
        {
            F1 = Ret(c, i, 1),
            F2 = Ret(c, i, 2),
            F3 = Ret(c, i, 3),
            F4 = Ret(c, i, 5),
            F5 = Ret(c, i, 10),
            F6 = fSmaGap5,
            F7 = fSmaGap10,
            F8 = bodyPct,
            F9 = rangePct,
            F10 = inRange,
            F11 = volRatio,
            F12 = distSup,
            F13 = distRes,
            F14 = ema9gap,
            F15 = ema21gap,
            F16 = vwDev,
        };
    }

    private static decimal RollingTypicalVwap(IReadOnlyList<KiteHistoricalCandlePointDto> c, int i, int window)
    {
        decimal numerator = 0;
        decimal denominator = 0;
        for (var k = 0; k < window && i - k >= 0; k++)
        {
            var bar = c[i - k];
            var tp =
                bar.High + bar.Low + bar.Close == 0
                    ? bar.Close
                    : (bar.High + bar.Low + bar.Close) / 3m;
            var v = Math.Max(bar.Volume, 0L);
            numerator += tp * v;
            denominator += v;
        }

        return denominator == 0 ? c[i].Close : numerator / denominator;
    }

    private sealed class TbExample
    {
        public float F1 { get; set; }
        public float F2 { get; set; }
        public float F3 { get; set; }
        public float F4 { get; set; }
        public float F5 { get; set; }
        public float F6 { get; set; }
        public float F7 { get; set; }
        public float F8 { get; set; }
        public float F9 { get; set; }
        public float F10 { get; set; }
        public float F11 { get; set; }
        public float F12 { get; set; }
        public float F13 { get; set; }
        public float F14 { get; set; }
        public float F15 { get; set; }
        public float F16 { get; set; }
        public bool Label { get; set; }
    }

    private sealed class TripleBarrierFeatures
    {
        public float F1 { get; set; }
        public float F2 { get; set; }
        public float F3 { get; set; }
        public float F4 { get; set; }
        public float F5 { get; set; }
        public float F6 { get; set; }
        public float F7 { get; set; }
        public float F8 { get; set; }
        public float F9 { get; set; }
        public float F10 { get; set; }
        public float F11 { get; set; }
        public float F12 { get; set; }
        public float F13 { get; set; }
        public float F14 { get; set; }
        public float F15 { get; set; }
        public float F16 { get; set; }
    }

    private sealed class BarrierProbOutput
    {
        [ColumnName("PredictedLabel")]
        public bool Prediction { get; set; }

        [ColumnName("Probability")]
        public float Probability { get; set; }
    }
}
