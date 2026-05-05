using System.Globalization;
using System.Net;
using System.Text.Json;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Broker;

public sealed class KiteInstrumentsClient : IKiteInstrumentsClient
{
    private readonly HttpClient _http;

    public KiteInstrumentsClient(HttpClient http) => _http = http;

    public async Task<KiteInstrumentsFetchResult> FetchExchangeInstrumentsAsync(
        string exchange,
        string apiKey,
        string accessToken,
        int? maxRows,
        CancellationToken ct = default)
    {
        var ex = Uri.EscapeDataString(exchange);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"instruments/{ex}");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for instruments/{exchange}.";
            return new KiteInstrumentsFetchResult(false, msg, Array.Empty<KiteInstrumentListItemDto>(), false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var header = await reader.ReadLineAsync(ct);
        if (header is null)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        var items = new List<KiteInstrumentListItemDto>();

        if (maxRows is null)
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                var row = TryParseRow(line);
                if (row is not null)
                    items.Add(row);
            }

            return new KiteInstrumentsFetchResult(true, null, items, false);
        }

        if (maxRows is not int cap || cap < 1)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        while (items.Count < cap)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                return new KiteInstrumentsFetchResult(true, null, items, false);

            var row = TryParseRow(line);
            if (row is not null)
                items.Add(row);
        }

        var truncated = await reader.ReadLineAsync(ct) is not null;
        return new KiteInstrumentsFetchResult(true, null, items, truncated);
    }

    public async Task<KiteInstrumentsFetchResult> SearchExchangeInstrumentsAsync(
        string exchange,
        string apiKey,
        string accessToken,
        string query,
        int maxMatches,
        CancellationToken ct = default)
    {
        if (maxMatches < 1)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        var needle = query.Trim().ToLowerInvariant();
        if (needle.Length == 0)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        var ex = Uri.EscapeDataString(exchange);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"instruments/{ex}");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for instruments/{exchange}.";
            return new KiteInstrumentsFetchResult(false, msg, Array.Empty<KiteInstrumentListItemDto>(), false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var header = await reader.ReadLineAsync(ct);
        if (header is null)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        var items = new List<KiteInstrumentListItemDto>();
        var scanTruncated = false;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var row = TryParseRow(line);
            if (row is null || !RowMatchesNeedle(row, needle))
                continue;

            items.Add(row);
            if (items.Count != maxMatches)
                continue;

            scanTruncated = await reader.ReadLineAsync(ct) is not null;
            break;
        }

        return new KiteInstrumentsFetchResult(true, null, items, scanTruncated);
    }

    private static bool RowMatchesNeedle(KiteInstrumentListItemDto row, string needleNormalized)
    {
        var haystack = string.Join(
                ' ',
                new[]
                {
                    row.Tradingsymbol,
                    row.Name ?? "",
                    row.Exchange,
                    row.InstrumentType ?? "",
                    row.Segment ?? "",
                    row.Expiry ?? "",
                    row.Strike?.ToString(CultureInfo.InvariantCulture) ?? "",
                    row.LotSize?.ToString(CultureInfo.InvariantCulture) ?? "",
                    row.InstrumentToken,
                })
            .ToLowerInvariant();
        return haystack.Contains(needleNormalized, StringComparison.Ordinal);
    }

    private static KiteInstrumentListItemDto? TryParseRow(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 12)
            return null;

        var token = parts[0].Trim();
        var tradingsymbol = parts[2].Trim();
        var exchange = parts[11].Trim();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(tradingsymbol) || string.IsNullOrEmpty(exchange))
            return null;

        var name = NullableTrim(parts[3]);
        var expiry = NullableTrim(parts[5]);
        var instrumentType = NullableTrim(parts[9]);
        var segment = NullableTrim(parts[10]);

        decimal? strike = null;
        if (decimal.TryParse(parts[6], CultureInfo.InvariantCulture, out var strikeVal))
            strike = strikeVal;

        int? lot = null;
        if (int.TryParse(parts[8], CultureInfo.InvariantCulture, out var lotVal))
            lot = lotVal;

        return new KiteInstrumentListItemDto(
            token,
            tradingsymbol,
            exchange,
            name,
            instrumentType,
            segment,
            expiry,
            strike,
            lot);
    }

    private static string? NullableTrim(string value)
    {
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    private static string? TryParseKiteMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("message", out var msg))
                return null;
            return msg.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
