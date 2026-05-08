namespace Trader.Application.Prediction;

public static class PriceDirectionOutcomeResolver
{
    /// <param name="labelThresholdFraction">
    /// Neutral band absolute return vs <paramref name="refClose"/> used for labeling and scoring (e.g. 0.0005 = 0.05%).
    /// Zero restores legacy sign-only behavior.
    /// </param>
    public static string Resolve(string direction, decimal refClose, decimal nextClose, decimal labelThresholdFraction = 0m)
    {
        var actual = PriceDirectionLabeling.ActualDirection(refClose, nextClose, labelThresholdFraction);
        if (direction == "neutral")
            return actual == "neutral" ? "correct" : "wrong";
        if (direction == "up")
            return actual == "up" ? "correct" : actual == "down" ? "wrong" : "wrong";
        if (direction == "down")
            return actual == "down" ? "correct" : actual == "up" ? "wrong" : "wrong";
        return "wrong";
    }
}
