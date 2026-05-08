using Trader.Application.Broker;

namespace Trader.Application.Prediction;

/// <summary>Find reference bar position in chronological Kite historic candles (+ optional gap heuristic).</summary>
public static class MlResolutionCandleLocator
{
    public static int FindRefBarIndex(IReadOnlyList<KiteHistoricalCandlePointDto> candles, DateTimeOffset refBar)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            if (Math.Abs((candles[i].Time - refBar).TotalSeconds) < 1.5)
                return i;
        }

        return -1;
    }

    /// <returns>True when the next bar opens more than roughly two bar lengths after the reference close time.</returns>
    public static bool LooksLikeSessionGap(TimeSpan nominalBarDuration, DateTimeOffset refBarTimeUtc, DateTimeOffset nextBarTimeUtc)
    {
        if (nominalBarDuration <= TimeSpan.Zero)
            return false;
        var elapsed = nextBarTimeUtc - refBarTimeUtc;
        return elapsed > nominalBarDuration * 2 + TimeSpan.FromSeconds(90);
    }
}
