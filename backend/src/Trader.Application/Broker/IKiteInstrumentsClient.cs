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
    /// </summary>
    Task<KiteInstrumentsFetchResult> SearchExchangeInstrumentsAsync(
        string exchange,
        string apiKey,
        string accessToken,
        string query,
        int maxMatches,
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
}

public sealed record KiteInstrumentsFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<KiteInstrumentListItemDto> Items,
    bool Truncated);

public sealed record KiteHistoricalFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<KiteHistoricalCandlePointDto> Candles);
