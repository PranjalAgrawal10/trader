namespace Trader.Application.Broker;

public sealed record KiteHistoricalOverlaysDto(
    IReadOnlyList<KiteHistoricalOverlayPointDto> Points,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>OHL-only candle for fast chart payloads; pair with <see cref="KiteHistoricalOverlaysDto"/>.</summary>
public sealed record KiteHistoricalOhlcvOnlyCandleDto(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

public sealed record KiteHistoricalOhlcvOnlyDto(
    IReadOnlyList<KiteHistoricalOhlcvOnlyCandleDto> Candles,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To);

/// <summary>Batch OHLC-only response across multiple requested intervals.</summary>
public sealed record KiteHistoricalOhlcvMultiDto(
    IReadOnlyList<KiteHistoricalOhlcvOnlyDto> Items);

public sealed record KiteHistoricalOverlayPointDto(
    DateTimeOffset Time,
    decimal? Sma20,
    decimal? Ema9,
    decimal? Ema21,
    decimal? SrSupport,
    decimal? SrResistance);

/// <summary>LTP-style snapshot via Kite quote/ohlc (small payload; cache ~5s server-side).</summary>
public sealed record KiteInstrumentLiveQuoteDto(
    string Exchange,
    string Tradingsymbol,
    decimal LastPrice,
    decimal PreviousClose);

/// <summary>OHLCV series from Kite historical API (possibly after server-side resampling for 2m / 4m).</summary>
public sealed record KiteHistoricalCandlesDto(
    IReadOnlyList<KiteHistoricalCandlePointDto> Candles,
    string Interval,
    DateTimeOffset From,
    DateTimeOffset To);

public sealed record KiteHistoricalCandlePointDto(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    /// <summary>SMA(20) on close; null when insufficient left history (client chart uses extended Kite fetch to warm up).</summary>
    decimal? Sma20 = null,
    decimal? Ema9 = null,
    decimal? Ema21 = null,
    /// <summary>Trailing min low over a 20-bar window (same length as SMA period).</summary>
    decimal? SrSupport = null,
    /// <summary>Trailing max high over a 20-bar window (same length as SMA period).</summary>
    decimal? SrResistance = null);
