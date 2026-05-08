namespace Trader.Application.Prediction;

/// <summary>Threshold-based next-bar labels for training/backtests (fraction of price).</summary>
public static class PriceDirectionLabeling
{
    /// <param name="labelThresholdFraction">Half-spread-style band: e.g. 0.0005 = 5 bps. Zero reproduces pure sign(close[t+1]-close[t]).</param>
    public static sbyte? ClassifySignedLabel(decimal refClose, decimal nextClose, decimal labelThresholdFraction)
    {
        if (refClose == 0)
            return null;
        var r = (nextClose - refClose) / Math.Abs(refClose);
        var t = Math.Max(0m, labelThresholdFraction);
        if (r > t)
            return 1;
        if (r < -t)
            return -1;
        return 0;
    }

    /// <returns><c>up</c>, <c>down</c>, or <c>neutral</c> aligned with training labels.</returns>
    public static string SignedToDirectionString(sbyte? signed) =>
        signed switch
        {
            1 => "up",
            -1 => "down",
            0 => "neutral",
            _ => "neutral",
        };

    /// <summary>Comparable to model output string for correctness vs reference move.</summary>
    public static string ActualDirection(decimal refClose, decimal nextClose, decimal labelThresholdFraction) =>
        SignedToDirectionString(ClassifySignedLabel(refClose, nextClose, labelThresholdFraction));
}
