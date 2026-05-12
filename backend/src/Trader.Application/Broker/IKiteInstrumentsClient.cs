namespace Trader.Application.Broker;

public interface IKiteInstrumentsClient
{
    /// <summary>
    /// Streams a Kite <c>/instruments/{exchange}</c> CSV response (gzip). When <paramref name="maxRows"/> is null, reads the full file.
    /// </summary>
    Task<KiteInstrumentsFetchResult> FetchExchangeInstrumentsAsync(
        string exchange,
        string apiKey,
        string accessToken,
        int? maxRows,
        CancellationToken ct = default);

    /// <summary>
    /// Streams Kite <c>/instruments/{exchange}</c> and returns rows whose searchable fields contain <paramref name="query"/> (case-insensitive). Stops after <paramref name="maxMatches"/> hits; <see cref="KiteInstrumentsFetchResult.Truncated"/> indicates the exchange dump may have more matches.
    /// When <paramref name="equityCashOnly"/> is true, keeps only NSE/BSE spot universe: cash equity (<c>instrument_type</c> EQ/BE/BZ), INDEX, and rows whose <c>segment</c> contains INDICES (Kite index listings — often EQ + segment INDICES).
    /// </summary>
    Task<KiteInstrumentsFetchResult> SearchExchangeInstrumentsAsync(
        string exchange,
        string apiKey,
        string accessToken,
        string query,
        int maxMatches,
        bool equityCashOnly = false,
        CancellationToken ct = default);

    /// <summary>
    /// Streams Kite <c>/instruments/{exchange}</c> until the row with matching <paramref name="instrumentToken"/> is found (authoritative <c>lot_size</c>).
    /// </summary>
    Task<KiteInstrumentsFetchResult> FetchInstrumentRowByTokenAsync(
        string exchange,
        string instrumentToken,
        string apiKey,
        string accessToken,
        CancellationToken ct = default);

    /// <summary>Kite <c>GET /instruments/historical/{token}/{interval}</c> — <paramref name="fromUtc"/> / <paramref name="toUtc"/> are converted to IST in the query string.</summary>
    Task<KiteHistoricalFetchResult> FetchHistoricalCandlesAsync(
        string instrumentToken,
        string kiteInterval,
        string apiKey,
        string accessToken,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool continuous,
        CancellationToken ct = default);

    /// <summary>Kite <c>GET /quote/ohlc</c> — up to <b>1000</b> instruments per HTTP call (<c>i=exchange:tradingsymbol</c>).</summary>
    Task<KiteQuoteOhlcFetchResult> FetchQuoteOhlcAsync(
        IReadOnlyList<string> exchangeTradingsymbolKeys,
        string apiKey,
        string accessToken,
        CancellationToken ct = default);
}

public sealed record KiteQuoteOhlcFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyDictionary<string, KiteQuoteOhlcTickDto> ByKey);

/// <summary>Partial fields from Kite quote/ohlc (see Kite docs <c>ohlc.close</c> = prior session).</summary>
public sealed record KiteQuoteOhlcTickDto(long InstrumentToken, decimal LastPrice, decimal OhlcOpen, decimal OhlcHigh, decimal OhlcLow, decimal OhlcClose);

public sealed record KiteInstrumentsFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<KiteInstrumentListItemDto> Items,
    bool Truncated);

public sealed record KiteHistoricalFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<KiteHistoricalCandlePointDto> Candles);
