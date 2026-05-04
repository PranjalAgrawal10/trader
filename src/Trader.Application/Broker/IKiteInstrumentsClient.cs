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
}

public sealed record KiteInstrumentsFetchResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<KiteInstrumentListItemDto> Items,
    bool Truncated);
