using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trader.Application.Broker;


namespace Trader.Infrastructure.Broker;

public sealed partial class KiteInstrumentsClient
{
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

}
