namespace Trader.Application.Prediction;

/// <summary>Nominal bar spacing for normalized UI/chart intervals (same codes as Kite candles).</summary>
public static class MlChartIntervalDuration
{
    /// <exception cref="InvalidOperationException">Unsupported interval token.</exception>
    public static TimeSpan FromNormalizedInterval(string normalizedInterval)
    {
        return normalizedInterval.Trim().ToLowerInvariant() switch
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
            "1d" => TimeSpan.FromDays(1),
            _ => throw new InvalidOperationException(
                $"Unknown interval '{normalizedInterval}' for bar duration."),
        };
    }
}
