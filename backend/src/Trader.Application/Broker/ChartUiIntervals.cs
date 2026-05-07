namespace Trader.Application.Broker;

/// <summary>Normalizes Kite instruments page candle interval UI codes (<c>1m</c> … <c>1d</c>).</summary>
public static class ChartUiIntervals
{
    public static string Normalize(string interval)
    {
        var t = interval.Trim().ToLowerInvariant();
        return t switch
        {
            "1m" or "2m" or "3m" or "4m" or "5m" or "10m" or "15m" or "30m" or "1h" or "1d" => t,
            _ => throw new InvalidOperationException(
                "Interval must be one of: 1m, 2m, 3m, 4m, 5m, 10m, 15m, 30m, 1h, 1d."),
        };
    }
}
