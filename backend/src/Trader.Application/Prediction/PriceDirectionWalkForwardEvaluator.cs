using Trader.Application.Broker;

namespace Trader.Application.Prediction;

public sealed record PriceDirectionWalkForwardMetrics(
    int Evaluated,
    int Correct,
    int Wrong,
    int NeutralPredictions,
    double Accuracy,
    double BalancedAccuracy,
    double MeanConfidence,
    double BrierScore,
    IReadOnlyDictionary<string, int> OutcomeCounts);

/// <summary>
/// Deterministic walk-forward evaluation helper over historical candles.
/// </summary>
public static class PriceDirectionWalkForwardEvaluator
{
    public static PriceDirectionWalkForwardMetrics Evaluate(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        IPriceDirectionPredictionEngine engine,
        decimal thresholdFraction,
        int minCandlesRequired = PriceDirectionPredictionService.MinCandlesRequired)
    {
        if (candles.Count < minCandlesRequired + 2)
            return Empty();

        var evaluated = 0;
        var correct = 0;
        var wrong = 0;
        var neutralPredictions = 0;
        var confSum = 0d;
        var brierSum = 0d;

        var upTotal = 0;
        var upCorrect = 0;
        var downTotal = 0;
        var downCorrect = 0;

        for (var i = minCandlesRequired - 1; i < candles.Count - 1; i++)
        {
            var window = candles.Take(i + 1).ToList();
            var pred = engine.PredictNextDirection(window);
            var actual = PriceDirectionLabeling.ClassifySignedLabel(
                candles[i].Close,
                candles[i + 1].Close,
                thresholdFraction);

            if (actual is not (1 or -1))
                continue;

            evaluated++;
            confSum += pred.Confidence;
            var pUp = Math.Clamp(pred.Confidence / 100d, 0d, 1d);
            var y = actual == 1 ? 1d : 0d;
            brierSum += Math.Pow(pUp - y, 2);

            if (actual == 1)
                upTotal++;
            else
                downTotal++;

            var predictedLabel = pred.Direction switch
            {
                PriceDirectionLabel.Up => 1,
                PriceDirectionLabel.Down => -1,
                _ => 0,
            };

            if (predictedLabel == 0)
            {
                neutralPredictions++;
                continue;
            }

            if (predictedLabel == actual)
            {
                correct++;
                if (actual == 1)
                    upCorrect++;
                else
                    downCorrect++;
            }
            else
            {
                wrong++;
            }
        }

        if (evaluated == 0)
            return Empty();

        var upRecall = upTotal == 0 ? 0 : (double)upCorrect / upTotal;
        var downRecall = downTotal == 0 ? 0 : (double)downCorrect / downTotal;
        var balanced = (upRecall + downRecall) / 2d;
        var accuracy = (double)correct / evaluated;
        var meanConfidence = confSum / evaluated;
        var brier = brierSum / evaluated;

        return new PriceDirectionWalkForwardMetrics(
            evaluated,
            correct,
            wrong,
            neutralPredictions,
            Accuracy: accuracy,
            BalancedAccuracy: balanced,
            MeanConfidence: meanConfidence,
            BrierScore: brier,
            OutcomeCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["upTotal"] = upTotal,
                ["downTotal"] = downTotal,
                ["upCorrect"] = upCorrect,
                ["downCorrect"] = downCorrect,
            });
    }

    private static PriceDirectionWalkForwardMetrics Empty() =>
        new(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}

