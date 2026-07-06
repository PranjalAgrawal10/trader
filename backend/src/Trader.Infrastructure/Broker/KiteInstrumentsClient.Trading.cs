using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trader.Application.Broker;


namespace Trader.Infrastructure.Broker;

public sealed partial class KiteInstrumentsClient
{
    public async Task<KiteOrdersFetchResult> FetchOrdersAsync(
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "orders");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for orders.";
            return new KiteOrdersFetchResult(false, msg, Array.Empty<KiteOrderListItemDto>());
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st)
                || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TryParseKiteMessage(body) ?? "Unexpected orders response from Kite.";
                return new KiteOrdersFetchResult(false, msg, Array.Empty<KiteOrderListItemDto>());
            }

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new KiteOrdersFetchResult(true, null, Array.Empty<KiteOrderListItemDto>());

            var items = new List<KiteOrderListItemDto>();
            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    continue;

                string? ReadString(string key) =>
                    row.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
                        ? v.ToString()
                        : null;

                int ReadInt(string key)
                {
                    if (!row.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
                        return 0;
                    if (v.ValueKind == JsonValueKind.Number)
                    {
                        if (v.TryGetInt32(out var i)) return i;
                        if (v.TryGetInt64(out var l)) return (int)l;
                        return (int)v.GetDouble();
                    }

                    return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0;
                }

                int? ReadNullableInt(string key)
                {
                    if (!row.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
                        return null;
                    if (v.ValueKind == JsonValueKind.Number)
                    {
                        if (v.TryGetInt32(out var i)) return i;
                        if (v.TryGetInt64(out var l)) return (int)l;
                        return (int)v.GetDouble();
                    }

                    return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : null;
                }

                decimal? ReadNullableDec(string key)
                {
                    if (!row.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
                        return null;
                    if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
                    return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                        ? d
                        : null;
                }

                var orderId = ReadString("order_id");
                if (string.IsNullOrWhiteSpace(orderId))
                    continue;

                items.Add(new KiteOrderListItemDto(
                    orderId,
                    ReadString("exchange_order_id"),
                    ReadString("parent_order_id"),
                    ReadString("status") ?? "",
                    ReadString("status_message"),
                    ReadString("status_message_raw"),
                    ReadString("tradingsymbol") ?? "",
                    ReadString("exchange") ?? "",
                    ReadString("transaction_type") ?? "",
                    ReadString("variety") ?? "",
                    ReadString("order_type") ?? "",
                    ReadString("product") ?? "",
                    ReadString("validity") ?? "",
                    ReadInt("quantity"),
                    ReadInt("filled_quantity"),
                    ReadInt("pending_quantity"),
                    ReadNullableInt("cancelled_quantity"),
                    ReadNullableDec("price"),
                    ReadNullableDec("trigger_price"),
                    ReadNullableDec("average_price"),
                    ReadString("tag"),
                    ReadString("order_timestamp"),
                    ReadString("exchange_update_timestamp")));
            }

            return new KiteOrdersFetchResult(true, null, items);
        }
        catch (JsonException)
        {
            return new KiteOrdersFetchResult(false, "Could not parse Kite orders response.", Array.Empty<KiteOrderListItemDto>());
        }
    }

    public async Task<KitePositionsFetchResult> FetchPositionsAsync(
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "portfolio/positions");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for positions.";
            return new KitePositionsFetchResult(false, msg, Array.Empty<KitePositionNetItemDto>());
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st)
                || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TryParseKiteMessage(body) ?? "Unexpected positions response from Kite.";
                return new KitePositionsFetchResult(false, msg, Array.Empty<KitePositionNetItemDto>());
            }

            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("net", out var net)
                || net.ValueKind != JsonValueKind.Array)
            {
                return new KitePositionsFetchResult(true, null, Array.Empty<KitePositionNetItemDto>());
            }

            var items = new List<KitePositionNetItemDto>();
            foreach (var row in net.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                    continue;

                string? ReadString(string key) =>
                    row.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
                        ? v.ToString()
                        : null;

                int ReadInt(string key)
                {
                    if (!row.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
                        return 0;
                    if (v.ValueKind == JsonValueKind.Number)
                    {
                        if (v.TryGetInt32(out var i)) return i;
                        if (v.TryGetInt64(out var l)) return (int)l;
                        return (int)v.GetDouble();
                    }

                    return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0;
                }

                var exchange = ReadString("exchange")?.Trim() ?? string.Empty;
                var tradingsymbol = ReadString("tradingsymbol")?.Trim() ?? string.Empty;
                var product = ReadString("product")?.Trim() ?? string.Empty;
                if (exchange.Length == 0 || tradingsymbol.Length == 0 || product.Length == 0)
                    continue;

                items.Add(new KitePositionNetItemDto(
                    exchange,
                    tradingsymbol,
                    product,
                    ReadInt("quantity")));
            }

            return new KitePositionsFetchResult(true, null, items);
        }
        catch (JsonException)
        {
            return new KitePositionsFetchResult(false, "Could not parse Kite positions response.", Array.Empty<KitePositionNetItemDto>());
        }
    }

    public async Task<KiteUserMarginsFetchResult> FetchUserMarginsAsync(
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "user/margins");
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for user margins.";
            return new KiteUserMarginsFetchResult(false, msg, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st)
                || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TryParseKiteMessage(body) ?? "Unexpected user margins response from Kite.";
                return new KiteUserMarginsFetchResult(false, msg, null);
            }

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return new KiteUserMarginsFetchResult(true, null, new KiteUserMarginsDto(null, null));

            KiteMarginSegmentDto? equity = null;
            KiteMarginSegmentDto? commodity = null;
            if (data.TryGetProperty("equity", out var equityEl))
                equity = TryParseMarginSegment(equityEl);
            if (data.TryGetProperty("commodity", out var commodityEl))
                commodity = TryParseMarginSegment(commodityEl);

            return new KiteUserMarginsFetchResult(true, null, new KiteUserMarginsDto(equity, commodity));
        }
        catch (JsonException)
        {
            return new KiteUserMarginsFetchResult(false, "Could not parse Kite user margins response.", null);
        }
    }

    public Task<KiteOrderActionResult> CancelOrderAsync(
        string variety,
        string orderId,
        string? parentOrderId,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        var v = variety.Trim();
        var oid = orderId.Trim();
        var path = $"orders/{Uri.EscapeDataString(v)}/{Uri.EscapeDataString(oid)}";
        if (!string.IsNullOrWhiteSpace(parentOrderId))
            path += $"?parent_order_id={Uri.EscapeDataString(parentOrderId.Trim())}";
        return SendOrderActionAsync(HttpMethod.Delete, path, content: null, apiKey, accessToken, ct);
    }

    public Task<KiteOrderActionResult> ModifyOrderAsync(
        string variety,
        string orderId,
        KiteOrderUpsertRequest request,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        var v = variety.Trim();
        var oid = orderId.Trim();
        var path = $"orders/{Uri.EscapeDataString(v)}/{Uri.EscapeDataString(oid)}";
        var content = BuildOrderUpsertContent(request);
        return SendOrderActionAsync(HttpMethod.Put, path, content, apiKey, accessToken, ct);
    }

    public Task<KiteOrderActionResult> PlaceOrderAsync(
        string variety,
        KiteOrderUpsertRequest request,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        var v = variety.Trim();
        var path = $"orders/{Uri.EscapeDataString(v)}";
        var content = BuildOrderUpsertContent(request);
        return SendOrderActionAsync(HttpMethod.Post, path, content, apiKey, accessToken, ct);
    }

    public Task<KiteGttActionResult> PlaceGttOcoAsync(
        KiteGttOcoRequest request,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        var content = BuildGttOcoContent(request);
        return SendGttActionAsync(HttpMethod.Post, "gtt/triggers", content, apiKey, accessToken, ct);
    }

    public Task<KiteGttActionResult> PlaceGttSingleAsync(
        KiteGttSingleRequest request,
        string apiKey,
        string accessToken,
        CancellationToken ct = default)
    {
        var content = BuildGttSingleContent(request);
        return SendGttActionAsync(HttpMethod.Post, "gtt/triggers", content, apiKey, accessToken, ct);
    }

    private async Task<KiteOrderActionResult> SendOrderActionAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string apiKey,
        string accessToken,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");
        if (content is not null)
            request.Content = content;

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for order action.";
            return new KiteOrderActionResult(false, msg, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st)
                || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TryParseKiteMessage(body) ?? "Unexpected order action response from Kite.";
                return new KiteOrderActionResult(false, msg, null);
            }

            string? orderId = null;
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("order_id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
                    orderId = idEl.ToString();
            }

            return new KiteOrderActionResult(true, null, orderId);
        }
        catch (JsonException)
        {
            return new KiteOrderActionResult(false, "Could not parse Kite order action response.", null);
        }
    }

    private async Task<KiteGttActionResult> SendGttActionAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string apiKey,
        string accessToken,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Kite-Version", "3");
        request.Headers.TryAddWithoutValidation("Authorization", $"token {apiKey}:{accessToken}");
        if (content is not null)
            request.Content = content;

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var msg = TryParseKiteMessage(body) ?? $"Kite returned {(int)response.StatusCode} for GTT action.";
            return new KiteGttActionResult(false, msg, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("status", out var st)
                || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                var msg = TryParseKiteMessage(body) ?? "Unexpected GTT action response from Kite.";
                return new KiteGttActionResult(false, msg, null);
            }

            string? triggerId = null;
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("trigger_id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
                    triggerId = idEl.ToString();
            }

            return new KiteGttActionResult(true, null, triggerId);
        }
        catch (JsonException)
        {
            return new KiteGttActionResult(false, "Could not parse Kite GTT action response.", null);
        }
    }
}
