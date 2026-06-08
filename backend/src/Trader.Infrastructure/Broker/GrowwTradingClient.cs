using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Broker;

public sealed class GrowwTradingClient : IGrowwTradingClient
{
    private const string ApiVersionHeader = "X-API-VERSION";
    private const string ApiVersionValue = "1.0";
    private readonly HttpClient _http;

    public GrowwTradingClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<GrowwOrderActionResult> PlaceOrderAsync(
        GrowwOrderCreateRequest request,
        string accessToken,
        CancellationToken ct = default)
    {
        using var http = new HttpRequestMessage(HttpMethod.Post, "order/create");
        http.Headers.TryAddWithoutValidation(ApiVersionHeader, ApiVersionValue);
        http.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        http.Content = JsonContent.Create(new
        {
            trading_symbol = request.TradingSymbol,
            quantity = request.Quantity,
            price = request.Price,
            trigger_price = request.TriggerPrice,
            validity = request.Validity,
            exchange = request.Exchange,
            segment = request.Segment,
            product = request.Product,
            order_type = request.OrderType,
            transaction_type = request.TransactionType,
            order_reference_id = request.OrderReferenceId,
        });

        using var res = await _http.SendAsync(http, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return new GrowwOrderActionResult(false, ParseGrowwError(body) ?? $"Groww returned {(int)res.StatusCode}.", null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (!string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                return new GrowwOrderActionResult(false, ParseGrowwError(body) ?? "Unexpected Groww order response.", null, null, null);

            if (!doc.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                return new GrowwOrderActionResult(false, "Groww order payload missing.", null, null, null);

            var id = payload.TryGetProperty("groww_order_id", out var idEl) ? idEl.ToString() : null;
            var orderStatus = payload.TryGetProperty("order_status", out var os) ? os.ToString() : null;
            var remark = payload.TryGetProperty("remark", out var rm) ? rm.ToString() : null;
            return new GrowwOrderActionResult(true, null, id, orderStatus, remark);
        }
        catch (JsonException)
        {
            return new GrowwOrderActionResult(false, "Could not parse Groww order response.", null, null, null);
        }
    }

    public async Task<GrowwPositionsFetchResult> FetchPositionsAsync(
        string accessToken,
        string? segment,
        CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(segment)
            ? "positions/user"
            : $"positions/user?segment={Uri.EscapeDataString(segment.Trim().ToUpperInvariant())}";
        using var http = new HttpRequestMessage(HttpMethod.Get, path);
        http.Headers.TryAddWithoutValidation(ApiVersionHeader, ApiVersionValue);
        http.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");

        using var res = await _http.SendAsync(http, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            return new GrowwPositionsFetchResult(false, ParseGrowwError(body) ?? $"Groww returned {(int)res.StatusCode}.", Array.Empty<GrowwPositionItem>());
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (!string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                return new GrowwPositionsFetchResult(false, ParseGrowwError(body) ?? "Unexpected Groww positions response.", Array.Empty<GrowwPositionItem>());

            if (!doc.RootElement.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("positions", out var positions)
                || positions.ValueKind != JsonValueKind.Array)
            {
                return new GrowwPositionsFetchResult(true, null, Array.Empty<GrowwPositionItem>());
            }

            var list = new List<GrowwPositionItem>();
            foreach (var p in positions.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object)
                    continue;
                var exchange = p.TryGetProperty("exchange", out var ex) ? ex.ToString() : "";
                var tradingSymbol = p.TryGetProperty("trading_symbol", out var ts) ? ts.ToString() : "";
                var product = p.TryGetProperty("product", out var pr) ? pr.ToString() : "";
                var quantity = ReadInt(p, "quantity");
                if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(tradingSymbol) || string.IsNullOrWhiteSpace(product))
                    continue;
                list.Add(new GrowwPositionItem(exchange.Trim(), tradingSymbol.Trim(), product.Trim(), quantity));
            }

            return new GrowwPositionsFetchResult(true, null, list);
        }
        catch (JsonException)
        {
            return new GrowwPositionsFetchResult(false, "Could not parse Groww positions response.", Array.Empty<GrowwPositionItem>());
        }
    }

    private static int ReadInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
            return 0;
        if (v.ValueKind == JsonValueKind.Number)
        {
            if (v.TryGetInt32(out var i)) return i;
            if (v.TryGetInt64(out var l)) return (int)l;
            return (int)v.GetDouble();
        }

        return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static string? ParseGrowwError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.ToString();
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.ToString();
            if (doc.RootElement.TryGetProperty("remark", out var rem))
                return rem.ToString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
