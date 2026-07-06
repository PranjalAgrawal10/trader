using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trader.Application.Broker;


namespace Trader.Infrastructure.Broker;

public sealed partial class KiteInstrumentsClient
{
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

    /// <inheritdoc />
    public async Task<KiteInstrumentsFetchResult> FetchInstrumentRowByTokenAsync(
        string exchange,
        string instrumentToken,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchange))
            return new KiteInstrumentsFetchResult(false, "exchange is required.", Array.Empty<KiteInstrumentListItemDto>(), false);

        var tokenNeedle = instrumentToken.Trim();
        if (tokenNeedle.Length == 0 || !tokenNeedle.All(char.IsAsciiDigit))
            return new KiteInstrumentsFetchResult(false, "A numeric instrument_token is required.", Array.Empty<KiteInstrumentListItemDto>(), false);

        var exRaw = exchange.Trim();
        var exUpper = exRaw.ToUpperInvariant();
        var exEscaped = Uri.EscapeDataString(exRaw);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"instruments/{exEscaped}");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new KiteInstrumentsFetchResult(false, $"instruments/{exUpper}: not found.", Array.Empty<KiteInstrumentListItemDto>(), false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for instruments/{exUpper}.";
            return new KiteInstrumentsFetchResult(false, msg, Array.Empty<KiteInstrumentListItemDto>(), false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        if (await reader.ReadLineAsync(ct) is null)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var row = TryParseRow(line);
            if (row is null || !string.Equals(row.InstrumentToken, tokenNeedle, StringComparison.Ordinal))
                continue;

            if (!string.Equals(row.Exchange.Trim(), exUpper, StringComparison.OrdinalIgnoreCase))
                continue;

            return new KiteInstrumentsFetchResult(true, null, new[] { row }, false);
        }

        return new KiteInstrumentsFetchResult(
            false,
            $"Instrument token {tokenNeedle} was not found on exchange {exUpper}.",
            Array.Empty<KiteInstrumentListItemDto>(),
            false);
    }

    /// <inheritdoc />
    public async Task<KiteInstrumentsFetchResult> FetchInstrumentRowByTradingsymbolAsync(
        string exchange,
        string tradingsymbol,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchange))
            return new KiteInstrumentsFetchResult(false, "exchange is required.", Array.Empty<KiteInstrumentListItemDto>(), false);

        var symbolNeedle = tradingsymbol.Trim();
        if (symbolNeedle.Length == 0)
            return new KiteInstrumentsFetchResult(false, "tradingsymbol is required.", Array.Empty<KiteInstrumentListItemDto>(), false);

        var exRaw = exchange.Trim();
        var exUpper = exRaw.ToUpperInvariant();
        var exEscaped = Uri.EscapeDataString(exRaw);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"instruments/{exEscaped}");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new KiteInstrumentsFetchResult(false, $"instruments/{exUpper}: not found.", Array.Empty<KiteInstrumentListItemDto>(), false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for instruments/{exUpper}.";
            return new KiteInstrumentsFetchResult(false, msg, Array.Empty<KiteInstrumentListItemDto>(), false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        if (await reader.ReadLineAsync(ct) is null)
            return new KiteInstrumentsFetchResult(true, null, Array.Empty<KiteInstrumentListItemDto>(), false);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var row = TryParseRow(line);
            if (row is null || !string.Equals(row.Tradingsymbol, symbolNeedle, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(row.Exchange.Trim(), exUpper, StringComparison.OrdinalIgnoreCase))
                continue;

            return new KiteInstrumentsFetchResult(true, null, new[] { row }, false);
        }

        return new KiteInstrumentsFetchResult(
            false,
            $"Tradingsymbol {symbolNeedle} was not found on exchange {exUpper}.",
            Array.Empty<KiteInstrumentListItemDto>(),
            false);
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

        decimal? tick = null;
        if (decimal.TryParse(parts[7], CultureInfo.InvariantCulture, out var tickVal) && tickVal > 0)
            tick = tickVal;

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
            lot,
            tick);
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

    private static KiteMarginSegmentDto? TryParseMarginSegment(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
            return null;

        var enabled = row.TryGetProperty("enabled", out var enabledEl)
                      && enabledEl.ValueKind == JsonValueKind.True;

        if (!TryReadDecimalProperty(row, "net", out var net))
            net = 0m;

        decimal availableCash = 0m;
        decimal liveBalance = 0m;
        decimal openingBalance = 0m;
        decimal intradayPayin = 0m;
        if (row.TryGetProperty("available", out var availableEl) && availableEl.ValueKind == JsonValueKind.Object)
        {
            TryReadDecimalProperty(availableEl, "cash", out availableCash);
            TryReadDecimalProperty(availableEl, "live_balance", out liveBalance);
            TryReadDecimalProperty(availableEl, "opening_balance", out openingBalance);
            TryReadDecimalProperty(availableEl, "intraday_payin", out intradayPayin);
        }

        decimal utilisedDebits = 0m;
        if (row.TryGetProperty("utilised", out var utilisedEl) && utilisedEl.ValueKind == JsonValueKind.Object)
            TryReadDecimalProperty(utilisedEl, "debits", out utilisedDebits);

        return new KiteMarginSegmentDto(
            enabled,
            net,
            availableCash,
            liveBalance,
            openingBalance,
            intradayPayin,
            utilisedDebits);
    }

    private static bool TryReadDecimalProperty(JsonElement parent, string key, out decimal value)
    {
        value = 0m;
        if (!parent.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null)
            return false;

        if (el.ValueKind == JsonValueKind.Number)
        {
            value = el.GetDecimal();
            return true;
        }

        return decimal.TryParse(el.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
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

    private static FormUrlEncodedContent BuildOrderUpsertContent(KiteOrderUpsertRequest request)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("exchange", request.Exchange.Trim()),
            new("tradingsymbol", request.Tradingsymbol.Trim()),
            new("transaction_type", request.TransactionType.Trim()),
            new("quantity", request.Quantity.ToString(CultureInfo.InvariantCulture)),
            new("product", request.Product.Trim()),
            new("order_type", request.OrderType.Trim()),
            new("validity", request.Validity.Trim()),
        };

        if (request.Price.HasValue)
            values.Add(new("price", request.Price.Value.ToString(CultureInfo.InvariantCulture)));
        if (request.TriggerPrice.HasValue)
            values.Add(new("trigger_price", request.TriggerPrice.Value.ToString(CultureInfo.InvariantCulture)));
        if (request.DisclosedQuantity.HasValue)
            values.Add(new("disclosed_quantity", request.DisclosedQuantity.Value.ToString(CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(request.Tag))
            values.Add(new("tag", request.Tag.Trim()));
        if (request.MarketProtection.HasValue)
            values.Add(new("market_protection", request.MarketProtection.Value.ToString(CultureInfo.InvariantCulture)));

        return new FormUrlEncodedContent(values);
    }

    private static FormUrlEncodedContent BuildGttOcoContent(KiteGttOcoRequest request)
    {
        var condition = JsonSerializer.Serialize(new
        {
            exchange = request.Exchange.Trim(),
            tradingsymbol = request.Tradingsymbol.Trim(),
            trigger_values = new[] { request.LowerTriggerPrice, request.UpperTriggerPrice },
            last_price = request.LastPrice,
        });
        var exitSide = request.ExitTransactionType.Trim();
        var orders = JsonSerializer.Serialize(new object[]
        {
            new
            {
                exchange = request.Exchange.Trim(),
                tradingsymbol = request.Tradingsymbol.Trim(),
                transaction_type = exitSide,
                quantity = request.Quantity,
                order_type = "LIMIT",
                product = request.Product.Trim(),
                price = request.LowerTriggerPrice,
            },
            new
            {
                exchange = request.Exchange.Trim(),
                tradingsymbol = request.Tradingsymbol.Trim(),
                transaction_type = exitSide,
                quantity = request.Quantity,
                order_type = "LIMIT",
                product = request.Product.Trim(),
                price = request.UpperTriggerPrice,
            },
        });

        return new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("type", "two-leg"),
            new KeyValuePair<string, string>("condition", condition),
            new KeyValuePair<string, string>("orders", orders),
        });
    }

    private static FormUrlEncodedContent BuildGttSingleContent(KiteGttSingleRequest request)
    {
        var condition = JsonSerializer.Serialize(new
        {
            exchange = request.Exchange.Trim(),
            tradingsymbol = request.Tradingsymbol.Trim(),
            trigger_values = new[] { request.TriggerPrice },
            last_price = request.LastPrice,
        });
        var exitSide = request.ExitTransactionType.Trim();
        var orders = JsonSerializer.Serialize(new object[]
        {
            new
            {
                exchange = request.Exchange.Trim(),
                tradingsymbol = request.Tradingsymbol.Trim(),
                transaction_type = exitSide,
                quantity = request.Quantity,
                order_type = "LIMIT",
                product = request.Product.Trim(),
                price = request.TriggerPrice,
            },
        });

        return new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("type", "single"),
            new KeyValuePair<string, string>("condition", condition),
            new KeyValuePair<string, string>("orders", orders),
        });
    }

}
