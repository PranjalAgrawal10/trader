namespace Trader.Application.Broker;

/// <summary>Pure helpers for Opening ATM GTT prices (percent of entry premium) and legacy trail math.</summary>
public static class NiftyOpenAutoTradeTrail
{
    public static decimal ClampTrailPoints(decimal trailPoints) =>
        ClampGttPercent(trailPoints);

    /// <summary>GTT −ve/+ve percent of entry premium (default 5, max 50).</summary>
    public static decimal ClampGttPercent(decimal percent) =>
        percent <= 0 ? 5m : Math.Min(percent, 50m);

    /// <summary>Legacy alias — values are treated as percent.</summary>
    public static decimal ClampGttPoints(decimal points) =>
        ClampGttPercent(points);

    public static decimal InitialStopPriceFromPercent(decimal entryPrice, decimal stopLossPercent, decimal tickSize)
    {
        var pct = ClampGttPercent(stopLossPercent);
        var raw = entryPrice * (1m - pct / 100m);
        if (raw <= 0)
            raw = tickSize > 0 ? tickSize : 0.05m;
        return KiteTickPriceRounding.RoundToTickSize(raw, tickSize);
    }

    public static decimal InitialTargetPriceFromPercent(decimal entryPrice, decimal targetPercent, decimal tickSize)
    {
        var pct = ClampGttPercent(targetPercent);
        return KiteTickPriceRounding.RoundToTickSize(entryPrice * (1m + pct / 100m), tickSize);
    }

    /// <summary>Legacy point-distance stop (kept for trail cycle helpers).</summary>
    public static decimal InitialStopPrice(decimal entryPrice, decimal trailPoints, decimal tickSize)
    {
        var pts = ClampTrailPoints(trailPoints);
        var raw = entryPrice - pts;
        if (raw <= 0)
            raw = tickSize > 0 ? tickSize : 0.05m;
        return KiteTickPriceRounding.RoundToTickSize(raw, tickSize);
    }

    public static decimal InitialTargetPrice(decimal entryPrice, decimal targetPoints, decimal tickSize) =>
        InitialTargetPriceFromPercent(entryPrice, targetPoints, tickSize);

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
