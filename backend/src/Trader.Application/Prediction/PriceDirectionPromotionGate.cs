namespace Trader.Application.Prediction;

public sealed record PriceDirectionPromotionGate(
    double MinAccuracyLift = 0.01,
    double MinBalancedAccuracyLift = 0.01,
    double MaxBrierScoreWorsening = 0.01,
    int MinEvaluatedRows = 150);

public sealed record PriceDirectionPromotionGateDecision(
    bool Accepted,
    string Reason);

public static class PriceDirectionPromotionGateEvaluator
{
    public static PriceDirectionPromotionGateDecision Evaluate(
        PriceDirectionWalkForwardMetrics candidate,
        PriceDirectionWalkForwardMetrics baseline,
        PriceDirectionPromotionGate? gate = null)
    {
        var g = gate ?? new PriceDirectionPromotionGate();
        if (candidate.Evaluated < g.MinEvaluatedRows || baseline.Evaluated < g.MinEvaluatedRows)
            return new(false, "insufficient_rows");

        var accLift = candidate.Accuracy - baseline.Accuracy;
        var balLift = candidate.BalancedAccuracy - baseline.BalancedAccuracy;
        var brierDiff = candidate.BrierScore - baseline.BrierScore;

        if (accLift < g.MinAccuracyLift)
            return new(false, "accuracy_lift_below_gate");
        if (balLift < g.MinBalancedAccuracyLift)
            return new(false, "balanced_accuracy_lift_below_gate");
        if (brierDiff > g.MaxBrierScoreWorsening)
            return new(false, "brier_worsened_too_much");

        return new(true, "accepted");
    }
}

