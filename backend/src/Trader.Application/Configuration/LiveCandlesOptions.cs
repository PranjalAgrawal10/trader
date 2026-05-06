namespace Trader.Application.Configuration;

/// <summary>Live tick → OHLC aggregation written to <see cref="Trader.Domain.Entities.HistoricalCandle"/>.</summary>
public sealed class LiveCandlesOptions
{
    public const string SectionName = "LiveCandles";

    /// <summary>When false, ticks are not aggregated (e.g. integration tests, InMemory DB).</summary>
    public bool Enabled { get; set; } = true;
}
