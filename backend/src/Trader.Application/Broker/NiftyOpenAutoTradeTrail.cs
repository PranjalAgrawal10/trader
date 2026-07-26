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
    /// Raises the stop when LTP makes a new peak, keeping SL at <paramref name="trailPercent"/> below peak.
    /// </summary>
    public static (decimal NewPeak, decimal? NewStop) ComputeTrailUpdateFromPercent(
        decimal peakPrice,
        decimal currentStop,
        decimal ltp,
        decimal trailPercent,
        decimal tickSize)
    {
        var newPeak = ltp > peakPrice ? ltp : peakPrice;
        var desired = InitialStopPriceFromPercent(newPeak, trailPercent, tickSize);
        if (desired > currentStop)
            return (newPeak, desired);
        return (newPeak, null);
    }

    /// <summary>Default: LTP within 2% below the +ve GTT trigger counts as “about to trigger”.</summary>
    public const decimal DefaultTargetApproachProximityPercent = 2m;

    /// <summary>When approaching +ve GTT, raise target to LTP × (1 + this % / 100).</summary>
    public const decimal DefaultTargetApproachBumpPercent = 10m;

    /// <summary>When approaching +ve GTT, move SL to LTP × (1 − this % / 100).</summary>
    public const decimal DefaultTargetApproachStopPercent = 5m;

    /// <summary>
    /// True when LTP is within <paramref name="proximityPercent"/> of the +ve trigger from below
    /// (or already at/through it while the GTT may still be live).
    /// </summary>
    public static bool IsApproachingTargetTrigger(
        decimal ltp,
        decimal targetTriggerPrice,
        decimal proximityPercent = DefaultTargetApproachProximityPercent)
    {
        if (ltp <= 0 || targetTriggerPrice <= 0)
            return false;
        var prox = proximityPercent <= 0 ? DefaultTargetApproachProximityPercent : proximityPercent;
        var approachFloor = targetTriggerPrice * (1m - prox / 100m);
        return ltp >= approachFloor;
    }

    /// <summary>
    /// When LTP is about to hit +ve GTT: new target = LTP + <paramref name="targetBumpPercent"/>%,
    /// new SL = LTP − <paramref name="stopPercent"/>% (ratchet only — never lower target or SL).
    /// </summary>
    public static (decimal? NewTarget, decimal? NewStop) ComputeTargetApproachBump(
        decimal ltp,
        decimal currentTarget,
        decimal currentStop,
        decimal tickSize,
        decimal targetBumpPercent = DefaultTargetApproachBumpPercent,
        decimal stopPercent = DefaultTargetApproachStopPercent,
        decimal proximityPercent = DefaultTargetApproachProximityPercent)
    {
        if (!IsApproachingTargetTrigger(ltp, currentTarget, proximityPercent))
            return (null, null);

        var bumpPct = targetBumpPercent <= 0 ? DefaultTargetApproachBumpPercent : targetBumpPercent;
        var slPct = stopPercent <= 0 ? DefaultTargetApproachStopPercent : stopPercent;

        var desiredTarget = KiteTickPriceRounding.RoundToTickSize(ltp * (1m + bumpPct / 100m), tickSize);
        var desiredStop = InitialStopPriceFromPercent(ltp, slPct, tickSize);

        decimal? newTarget = desiredTarget > currentTarget ? desiredTarget : null;
        decimal? newStop = desiredStop > currentStop ? desiredStop : null;
        return (newTarget, newStop);
    }

    /// <summary>
    /// Legacy point-gap trail. Prefer <see cref="ComputeTrailUpdateFromPercent"/> for Opening ATM.
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
