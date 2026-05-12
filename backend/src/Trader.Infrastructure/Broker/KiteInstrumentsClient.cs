using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Broker;

public sealed partial class KiteInstrumentsClient : IKiteInstrumentsClient
{
    private static readonly TimeZoneInfo IndiaTz = ResolveIndiaTimeZone();

    private readonly HttpClient _http;

    public KiteInstrumentsClient(HttpClient http) => _http = http;

    public async Task<KiteHistoricalFetchResult> FetchHistoricalCandlesAsync(
        string instrumentToken,
        string kiteInterval,
        string apiKey,
        string accessToken,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        bool continuous,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken))
            return new KiteHistoricalFetchResult(false, "instrument token is required.", Array.Empty<KiteHistoricalCandlePointDto>());

        var token = instrumentToken.Trim();
        var interval = kiteInterval.Trim();
        var fromIst = TimeZoneInfo.ConvertTimeFromUtc(fromUtc.UtcDateTime, IndiaTz);
        var toIst = TimeZoneInfo.ConvertTimeFromUtc(toUtc.UtcDateTime, IndiaTz);
        var fromQ = Uri.EscapeDataString(fromIst.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var toQ = Uri.EscapeDataString(toIst.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var cont = continuous ? "1" : "0";
        var path =
            $"instruments/historical/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(interval)}?from={fromQ}&to={toQ}&continuous={cont}&oi=0";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for historical data.";
            return new KiteHistoricalFetchResult(false, msg, Array.Empty<KiteHistoricalCandlePointDto>());
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st) || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TryParseKiteMessage(body) ?? "Unexpected historical response from Kite.";
                return new KiteHistoricalFetchResult(false, msg, Array.Empty<KiteHistoricalCandlePointDto>());
            }

            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("candles", out var candlesEl)
                || candlesEl.ValueKind != JsonValueKind.Array)
            {
                return new KiteHistoricalFetchResult(true, null, Array.Empty<KiteHistoricalCandlePointDto>());
            }

            var list = new List<KiteHistoricalCandlePointDto>();
            foreach (var row in candlesEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5)
                    continue;

                var ts = row[0].GetString();
                if (string.IsNullOrEmpty(ts) || !DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
                    continue;

                var o = row[1].GetDecimal();
                var h = row[2].GetDecimal();
                var l = row[3].GetDecimal();
                var c = row[4].GetDecimal();
                long volume = row.GetArrayLength() > 5 && row[5].ValueKind == JsonValueKind.Number
                    ? row[5].TryGetInt64(out var v) ? v : (long)row[5].GetDouble()
                    : 0L;

                list.Add(new KiteHistoricalCandlePointDto(time, o, h, l, c, volume));
            }

            return new KiteHistoricalFetchResult(true, null, list);
        }
        catch (JsonException)
        {
            return new KiteHistoricalFetchResult(false, "Could not parse Kite historical response.", Array.Empty<KiteHistoricalCandlePointDto>());
        }
    }

    private const int KiteQuoteOhlcBatchSize = 140;

    public async Task<KiteQuoteOhlcFetchResult> FetchQuoteOhlcAsync(
        IReadOnlyList<string> exchangeTradingsymbolKeys,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        if (exchangeTradingsymbolKeys.Count == 0)
            return new KiteQuoteOhlcFetchResult(true, null, new Dictionary<string, KiteQuoteOhlcTickDto>(StringComparer.Ordinal));

        var combined = new Dictionary<string, KiteQuoteOhlcTickDto>(StringComparer.Ordinal);

        for (var off = 0; off < exchangeTradingsymbolKeys.Count; off += KiteQuoteOhlcBatchSize)
        {
            var count = Math.Min(KiteQuoteOhlcBatchSize, exchangeTradingsymbolKeys.Count - off);
            var qb = new StringBuilder("quote/ohlc?");
            for (var j = 0; j < count; j++)
            {
                var key = exchangeTradingsymbolKeys[off + j].Trim();
                if (j > 0)
                    qb.Append('&');
                qb.Append("i=").Append(Uri.EscapeDataString(key));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, qb.ToString());
            request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
            request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for quote OHLC.";
                return new KiteQuoteOhlcFetchResult(false, msg, combined);
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("status", out var st)
                    || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = TryParseKiteMessage(body) ?? "Unexpected quote OHLC response from Kite.";
                    return new KiteQuoteOhlcFetchResult(false, msg, combined);
                }

                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var prop in data.EnumerateObject())
                {
                    var name = prop.Name;
                    var el = prop.Value;
                    if (TryParseOhlcQuoteTick(el, out var tick))
                        combined[name] = tick;
                }
            }
            catch (JsonException)
            {
                return new KiteQuoteOhlcFetchResult(false, "Could not parse Kite quote OHLC response.", combined);
            }
        }

        return new KiteQuoteOhlcFetchResult(true, null, combined);
    }

    private static bool TryParseOhlcQuoteTick(JsonElement el, out KiteQuoteOhlcTickDto tick)
    {
        tick = default!;
        long tokenNumeric = 0;
        if (el.TryGetProperty("instrument_token", out var itk))
        {
            if (itk.ValueKind == JsonValueKind.Number)
                tokenNumeric = itk.GetInt64();
            else if (itk.ValueKind == JsonValueKind.String && long.TryParse(itk.GetString(), CultureInfo.InvariantCulture, out var tkn))
                tokenNumeric = tkn;
        }

        if (!el.TryGetProperty("last_price", out var ltp) || ltp.ValueKind != JsonValueKind.Number)
            return false;

        if (!el.TryGetProperty("ohlc", out var oh) || oh.ValueKind != JsonValueKind.Object)
            return false;

        if (!oh.TryGetProperty("open", out var oj)
            || !oh.TryGetProperty("high", out var hj)
            || !oh.TryGetProperty("low", out var lj)
            || !oh.TryGetProperty("close", out var cj))
            return false;

        var lastPrice = ltp.GetDecimal();

        decimal ReadDec(JsonElement e) =>
            e.ValueKind switch
            {
                JsonValueKind.Number => e.GetDecimal(),
                JsonValueKind.String when decimal.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var d) => d,
                _ => 0,
            };

        var ohlcOpen = ReadDec(oj);
        var ohlcHi = ReadDec(hj);
        var ohlcLo = ReadDec(lj);
        var ohlcClose = ReadDec(cj);

        tick = new KiteQuoteOhlcTickDto(tokenNumeric, lastPrice, ohlcOpen, ohlcHi, ohlcLo, ohlcClose);
        return true;
    }

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
        bool equityCashOnly = false,
        CancellationToken ct = default)
    {
        if (maxMatches < 1)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        var tokens = ExpandSearchTokens(query);
        if (tokens.Count == 0)
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
            if (row is null)
                continue;
            // Kite CSV: cash equities use instrument_type EQ; indices often use EQ + segment INDICES (see Kite forum / instruments docs). Type INDEX may also appear.
            if (equityCashOnly && !IsKiteSpotSearchRow(row))
                continue;
            if (!RowMatchesNeedle(row, tokens))
                continue;

            items.Add(row);
            if (items.Count != maxMatches)
                continue;

            scanTruncated = await reader.ReadLineAsync(ct) is not null;
            break;
        }

        return new KiteInstrumentsFetchResult(true, null, items, scanTruncated);
    }

    /// <summary>
    /// Kite spot (NSE+BSE) universe: cash EQ/BE/BZ, <c>instrument_type</c> INDEX if present, and indices marked <c>segment=INDICES</c> (often <c>instrument_type</c> EQ per Kite — same as stocks).
    /// </summary>
    private static bool IsKiteSpotSearchRow(KiteInstrumentListItemDto row)
    {
        if (row.Segment is not null
            && row.Segment.Contains("INDICES", StringComparison.OrdinalIgnoreCase))
            return true;

        var t = row.InstrumentType?.Trim().ToUpperInvariant();
        return t is "EQ" or "BE" or "BZ" or "INDEX";
    }

    /// <summary>
    /// Builds search tokens: explicit whitespace separates phrases (AND). Within each phrase, letter runs and digit runs become separate tokens so compact queries behave like spaced ones (e.g. <c>nifty12may</c> → nifty + 12 + may).
    /// </summary>
    private static List<string> ExpandSearchTokens(string query)
    {
        var t = query.Trim().ToLowerInvariant().Replace('+', ' ');
        var segments = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokens = new List<string>();
        foreach (var seg in segments)
        {
            if (seg.Length == 0)
                continue;

            foreach (Match m in CompactSearchTokenRegex().Matches(seg))
            {
                var raw = m.Value;
                if (raw.Length == 0)
                    continue;
                var normalized = ApplyInstrumentSearchAliases(raw);
                if (normalized.Length > 0)
                    tokens.Add(normalized);
            }
        }

        return tokens;
    }

    [GeneratedRegex(@"\d+|[a-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex CompactSearchTokenRegex();

    /// <summary>Minimal typo shortcuts users type without spaces.</summary>
    private static string ApplyInstrumentSearchAliases(string alphaNumericLowerToken)
    {
        return alphaNumericLowerToken switch
        {
            "nity" => "nifty",
            "bnfty" => "banknifty",
            _ => alphaNumericLowerToken,
        };
    }

    /// <summary>
    /// Every token must appear in the row haystack (order-independent).
    /// Tokens come from whitespace-separated segments; each segment is split into letter runs and digit runs.
    /// </summary>
    private static bool RowMatchesNeedle(KiteInstrumentListItemDto row, IReadOnlyList<string> tokens)
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

        foreach (var token in tokens)
        {
            if (token.Length == 0)
                continue;
            if (!haystack.Contains(token, StringComparison.Ordinal))
                return false;
        }

        return true;
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

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
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
