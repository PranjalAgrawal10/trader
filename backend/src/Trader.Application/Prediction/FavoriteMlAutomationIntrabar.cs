using System;

namespace Trader.Application.Prediction;

/// <summary>
/// Gates favorite automation <strong>new</strong> predictions so they can fire partway through the current bar
/// (Kite&apos;s last candle is usually still forming) without waiting for the full interval to complete.
/// </summary>
public static class FavoriteMlAutomationIntrabar
{
    /// <summary>
    /// When <paramref name="minSecondsAfterBarOpen"/> is 0, returns true. Otherwise returns true only if{' '}
    /// <paramref name="utcNow"/> is at least that many seconds after <paramref name="refBarOpenUtc"/>, capped so the
    /// delay never reaches a full bar length (so a prediction can still occur before the bar closes when Kite exposes it).
    /// </summary>
    public static bool IsReadyForNewPredictionOnRefBar(
        DateTimeOffset utcNow,
        DateTimeOffset refBarOpenUtc,
        TimeSpan barLength,
        int minSecondsAfterBarOpen)
    {
        var t = Math.Clamp(minSecondsAfterBarOpen, 0, 86400);
        if (t <= 0)
            return true;

        var barSec = Math.Max(1L, (long)Math.Floor(barLength.TotalSeconds));
        var maxDelaySec = (int)Math.Min(t, barSec - 1);
        if (maxDelaySec <= 0)
            return true;

        return utcNow >= refBarOpenUtc + TimeSpan.FromSeconds(maxDelaySec);
    }
}
