using System;
using System.Collections.Generic;
using System.Linq;

namespace Trader.Application.Broker;

/// <summary>Normalizes Kite instruments page candle interval UI codes (<c>1m</c> … <c>1w</c>).</summary>
public static class ChartUiIntervals
{
    /// <summary>Display / persistence order for trend-analysis multi-select (matches SPA toolbar).</summary>
    public static readonly IReadOnlyList<string> OrderedUiCodes = new[]
    {
        "1m", "2m", "3m", "4m", "5m", "10m", "15m", "30m", "1h", "90m", "2h", "4h", "8h", "1d", "1w",
    };

    /// <summary>
    /// Returns unique normalized codes in <see cref="OrderedUiCodes"/> order.
    /// When nothing valid remains, returns a full copy of <see cref="OrderedUiCodes"/> (same as SPA default-all).
    /// </summary>
    public static IReadOnlyList<string> NormalizeTrendAnalysisSelection(IEnumerable<string> raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in raw)
        {
            if (string.IsNullOrWhiteSpace(x))
                continue;
            try
            {
                set.Add(Normalize(x.Trim()));
            }
            catch (InvalidOperationException)
            {
                // skip unknown tokens
            }
        }

        var ordered = OrderedUiCodes.Where(set.Contains).ToList();
        return ordered.Count > 0 ? ordered : OrderedUiCodes.ToList();
    }

    public static string Normalize(string interval)
    {
        var t = interval.Trim().ToLowerInvariant();
        return t switch
        {
            "1m" or "2m" or "3m" or "4m" or "5m" or "10m" or "15m" or "30m" or "1h" or "90m" or "2h" or "4h" or "8h" or "1d" or "1w" => t,
            _ => throw new InvalidOperationException(
                "Interval must be one of: 1m, 2m, 3m, 4m, 5m, 10m, 15m, 30m, 1h, 90m, 2h, 4h, 8h, 1d, 1w."),
        };
    }

    /// <summary>
    /// Wall-clock span from one bar open to the next for a normalized UI interval (matches Kite chart bucketing in{' '}
    /// <see cref="BrokerService" />).
    /// </summary>
    public static TimeSpan BarDuration(string normalizedInterval)
    {
        var code = normalizedInterval.Trim().ToLowerInvariant();
        return code switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "2m" => TimeSpan.FromMinutes(2),
            "3m" => TimeSpan.FromMinutes(3),
            "4m" => TimeSpan.FromMinutes(4),
            "5m" => TimeSpan.FromMinutes(5),
            "10m" => TimeSpan.FromMinutes(10),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "90m" => TimeSpan.FromMinutes(90),
            "2h" => TimeSpan.FromHours(2),
            "4h" => TimeSpan.FromHours(4),
            "8h" => TimeSpan.FromHours(8),
            "1d" => TimeSpan.FromDays(1),
            "1w" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromMinutes(5),
        };
    }
}
