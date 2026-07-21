namespace Trader.Application.Broker;

/// <summary>Pure helpers for point-based trailing stop on a long option premium.</summary>
public static class NiftyOpenAutoTradeTrail
{
    public static decimal ClampTrailPoints(decimal trailPoints) =>
        ClampGttPoints(trailPoints);

    public static decimal ClampGttPoints(decimal points) =>
        points <= 0 ? 5m : Math.Min(points, 500m);

    public static decimal InitialStopPrice(decimal entryPrice, decimal trailPoints, decimal tickSize)
    {
        var pts = ClampTrailPoints(trailPoints);
        var raw = entryPrice - pts;
        if (raw <= 0)
            raw = tickSize > 0 ? tickSize : 0.05m;
        return KiteTickPriceRounding.RoundToTickSize(raw, tickSize);
    }

    public static decimal InitialTargetPrice(decimal entryPrice, decimal targetPoints, decimal tickSize)
    {
        var pts = ClampGttPoints(targetPoints);
        return KiteTickPriceRounding.RoundToTickSize(entryPrice + pts, tickSize);
    }

    /// <summary>
    /// Raises the stop when LTP makes a new peak. Returns the new peak always; <paramref name="newStop"/>
    /// is set only when the stop should move up.
    /// </summary>
    public static (decimal NewPeak, decimal? NewStop) ComputeTrailUpdate(
        decimal peakPrice,
        decimal currentStop,
        decimal ltp,
        decimal trailPoints,
        decimal tickSize)
    {
        var pts = ClampTrailPoints(trailPoints);
        var newPeak = ltp > peakPrice ? ltp : peakPrice;
        var desired = InitialStopPrice(newPeak, pts, tickSize);
        if (desired > currentStop)
            return (newPeak, desired);
        return (newPeak, null);
    }
}
