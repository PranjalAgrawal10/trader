namespace Trader.Domain.Entities;

/// <summary>Persisted OHLCV bar for an instrument (fed by Kite historical sync or the candle engine).</summary>
public class HistoricalCandle
{
    public Guid Id { get; set; }

    /// <summary>Kite <c>instrument_token</c> as string (same shape as favorites list).</summary>
    public string InstrumentToken { get; set; } = string.Empty;

    /// <summary>Candle interval (<c>1m</c>, <c>5m</c>, <c>1d</c>, …).</summary>
    public string Timeframe { get; set; } = string.Empty;

    /// <summary>Candle open time (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public long Volume { get; set; }
}
