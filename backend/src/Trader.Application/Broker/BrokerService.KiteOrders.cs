using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Configuration;
using Trader.Application.Wallet;
using Trader.Domain.Entities;


namespace Trader.Application.Broker;

public sealed partial class BrokerService
{
    public async Task<KiteOrderBookDto> GetKiteOrdersAsync(Guid userId, CancellationToken ct = default)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var fetched = await _kiteInstruments.FetchOrdersAsync(apiKey, accessToken, ct).ConfigureAwait(false);
        if (!fetched.Success)
            throw new InvalidOperationException(fetched.ErrorMessage ?? "Could not load orders from Kite.");

        var items = fetched.Items
            .Select(o => new KiteOrderBookItemDto(
                o.OrderId,
                o.ExchangeOrderId,
                o.ParentOrderId,
                o.Status,
                o.StatusMessage,
                o.StatusMessageRaw,
                o.Tradingsymbol,
                o.Exchange,
                o.TransactionType,
                o.Variety,
                o.OrderType,
                o.Product,
                o.Validity,
                o.Quantity,
                o.FilledQuantity,
                o.PendingQuantity,
                o.CancelledQuantity,
                o.Price,
                o.TriggerPrice,
                o.AveragePrice,
                o.Tag,
                o.OrderTimestamp,
                o.ExchangeUpdateTimestamp))
            .OrderByDescending(x => x.ExchangeUpdateTimestamp ?? x.OrderTimestamp ?? "")
            .ToList();
        return new KiteOrderBookDto(items);
    }

    public async Task<IReadOnlyList<KiteNetPositionDto>> GetKiteNetPositionsAsync(Guid userId, CancellationToken ct = default)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var fetched = await _kiteInstruments.FetchPositionsAsync(apiKey, accessToken, ct).ConfigureAwait(false);
        if (!fetched.Success)
            throw new InvalidOperationException(fetched.ErrorMessage ?? "Could not load positions from Kite.");

        return fetched.NetItems
            .Where(x => x.Quantity != 0)
            .Select(x => new KiteNetPositionDto(
                x.Exchange,
                x.Tradingsymbol,
                x.Product,
                x.Quantity))
            .ToList();
    }

    public async Task<KiteUserMarginsDto> GetKiteUserMarginsAsync(Guid userId, CancellationToken ct = default)
    {
        var status = await GetStatusAsync(userId, ct).ConfigureAwait(false);
        if (!status.Connected
            || !string.Equals(status.Provider, BrokerZerodha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Connect Zerodha to view Kite account balance.");
        }

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var fetched = await _kiteInstruments.FetchUserMarginsAsync(apiKey, accessToken, ct).ConfigureAwait(false);
        if (!fetched.Success || fetched.Margins is null)
            throw new InvalidOperationException(fetched.ErrorMessage ?? "Could not load Kite margins.");

        return fetched.Margins;
    }

    public async Task<IReadOnlyList<KiteNetPositionDto>> GetNetPositionsAsync(
        Guid userId,
        string? broker,
        CancellationToken ct = default)
    {
        var provider = await ResolveOrderBrokerAsync(userId, broker, ct).ConfigureAwait(false);
        if (provider == BrokerZerodha)
            return await GetKiteNetPositionsAsync(userId, ct).ConfigureAwait(false);

        if (provider == BrokerGroww)
        {
            var token = await _brokerSetup.GetBrokerAccessTokenAsync(userId, "Groww", ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Groww is not connected.");

            var fetched = await _growwTrading.FetchPositionsAsync(token, segment: null, ct).ConfigureAwait(false);
            if (!fetched.Success)
                throw new InvalidOperationException(fetched.ErrorMessage ?? "Could not load positions from Groww.");

            return fetched.Items
                .Where(x => x.Quantity != 0)
                .Select(x => new KiteNetPositionDto(
                    x.Exchange,
                    x.TradingSymbol,
                    x.Product,
                    x.Quantity))
                .ToList();
        }

        throw new InvalidOperationException($"Unsupported broker provider: {provider}.");
    }

    public async Task<KiteOrderActionResultDto> CancelKiteOrderAsync(
        Guid userId,
        string orderId,
        KiteOrderCancelRequestDto body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(orderId))
            throw new InvalidOperationException("orderId is required.");

        var oid = orderId.Trim();
        var variety = NormalizeKiteOrderVariety(body.Variety);
        var parentOrderId = string.IsNullOrWhiteSpace(body.ParentOrderId) ? null : body.ParentOrderId.Trim();

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var action = await _kiteInstruments.CancelOrderAsync(variety, oid, parentOrderId, apiKey, accessToken, ct).ConfigureAwait(false);
        if (!action.Success)
            throw new InvalidOperationException(action.ErrorMessage ?? "Could not cancel order on Kite.");

        return new KiteOrderActionResultDto(action.OrderId ?? oid, "cancel", "Order cancel request accepted.");
    }

    public async Task<KiteOrderActionResultDto> ModifyKiteOrderAsync(
        Guid userId,
        string orderId,
        KiteOrderModifyRequestDto body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(orderId))
            throw new InvalidOperationException("orderId is required.");
        if (body.Quantity < 1)
            throw new InvalidOperationException("quantity must be greater than zero.");

        var oid = orderId.Trim();
        var variety = NormalizeKiteOrderVariety(body.Variety);
        var request = new KiteOrderUpsertRequest(
            NormalizeRequired(body.Exchange, "exchange"),
            NormalizeRequired(body.Tradingsymbol, "tradingsymbol"),
            NormalizeRequired(body.TransactionType, "transactionType"),
            body.Quantity,
            NormalizeRequired(body.Product, "product"),
            NormalizeRequired(body.OrderType, "orderType"),
            NormalizeValidity(body.Validity),
            NormalizeNullablePrice(body.Price),
            NormalizeNullablePrice(body.TriggerPrice),
            body.DisclosedQuantity > 0 ? body.DisclosedQuantity : null,
            NormalizeOptional(body.Tag),
            NormalizeMarketProtection(NormalizeRequired(body.OrderType, "orderType"), body.MarketProtection));
        ValidateOrderTypePayload(request.OrderType, request.Price, request.TriggerPrice);

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var action = await _kiteInstruments.ModifyOrderAsync(variety, oid, request, apiKey, accessToken, ct).ConfigureAwait(false);
        if (!action.Success)
            throw new InvalidOperationException(action.ErrorMessage ?? "Could not modify order on Kite.");

        return new KiteOrderActionResultDto(action.OrderId ?? oid, "modify", "Order modify request accepted.");
    }

    public async Task<KiteOrderActionResultDto> RepeatKiteOrderAsync(
        Guid userId,
        string sourceOrderId,
        KiteOrderRepeatRequestDto body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(sourceOrderId))
            throw new InvalidOperationException("sourceOrderId is required.");

        var sourceId = sourceOrderId.Trim();
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var fetched = await _kiteInstruments.FetchOrdersAsync(apiKey, accessToken, ct).ConfigureAwait(false);
        if (!fetched.Success)
            throw new InvalidOperationException(fetched.ErrorMessage ?? "Could not load orders from Kite.");

        var source = fetched.Items.FirstOrDefault(x => string.Equals(x.OrderId, sourceId, StringComparison.Ordinal));
        if (source is null)
            throw new InvalidOperationException("Source order not found in today orderbook.");

        var variety = NormalizeKiteOrderVariety(string.IsNullOrWhiteSpace(body.Variety) ? source.Variety : body.Variety);
        var quantity = source.Quantity > 0 ? source.Quantity : source.PendingQuantity > 0 ? source.PendingQuantity : 0;
        if (quantity < 1)
            throw new InvalidOperationException("Source order does not have a valid quantity to repeat.");

        var request = new KiteOrderUpsertRequest(
            NormalizeRequired(source.Exchange, "exchange"),
            NormalizeRequired(source.Tradingsymbol, "tradingsymbol"),
            NormalizeRequired(source.TransactionType, "transactionType"),
            quantity,
            NormalizeRequired(source.Product, "product"),
            NormalizeRequired(source.OrderType, "orderType"),
            NormalizeValidity(source.Validity),
            NormalizeNullablePrice(source.Price),
            NormalizeNullablePrice(source.TriggerPrice),
            null,
            NormalizeOptional(source.Tag),
            NormalizeMarketProtection(NormalizeRequired(source.OrderType, "orderType"), null));

        var action = await _kiteInstruments.PlaceOrderAsync(variety, request, apiKey, accessToken, ct).ConfigureAwait(false);
        if (!action.Success)
            throw new InvalidOperationException(action.ErrorMessage ?? "Could not repeat order on Kite.");

        return new KiteOrderActionResultDto(action.OrderId ?? sourceId, "repeat", "Order repeat request accepted.");
    }

    public async Task<KiteOrderActionResultDto> PlaceKiteOrderAsync(
        Guid userId,
        KiteOrderPlaceRequestDto body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");
        if (body.Quantity < 1)
            throw new InvalidOperationException("quantity must be greater than zero.");

        var variety = NormalizeKiteOrderVariety(body.Variety);
        var request = new KiteOrderUpsertRequest(
            NormalizeRequired(body.Exchange, "exchange"),
            NormalizeRequired(body.Tradingsymbol, "tradingsymbol"),
            NormalizeRequired(body.TransactionType, "transactionType"),
            body.Quantity,
            NormalizeRequired(body.Product, "product"),
            NormalizeRequired(body.OrderType, "orderType"),
            NormalizeValidity(body.Validity),
            NormalizeNullablePrice(body.Price),
            NormalizeNullablePrice(body.TriggerPrice),
            body.DisclosedQuantity > 0 ? body.DisclosedQuantity : null,
            NormalizeOptional(body.Tag),
            NormalizeMarketProtection(NormalizeRequired(body.OrderType, "orderType"), body.MarketProtection));
        ValidateOrderTypePayload(request.OrderType, request.Price, request.TriggerPrice);

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var action = await _kiteInstruments.PlaceOrderAsync(variety, request, apiKey, accessToken, ct).ConfigureAwait(false);
        if (!action.Success)
            throw new InvalidOperationException(action.ErrorMessage ?? "Could not place order on Kite.");

        return new KiteOrderActionResultDto(action.OrderId ?? "unknown", "place", "Order placement request accepted.");
    }

    public async Task<KiteGttActionResultDto> CreateKiteGttOcoAsync(
        Guid userId,
        KiteGttCreateRequestDto body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");
        if (body.Quantity < 1)
            throw new InvalidOperationException("quantity must be greater than zero.");

        var exchange = NormalizeRequired(body.Exchange, "exchange").ToUpperInvariant();
        var tradingsymbol = NormalizeRequired(body.Tradingsymbol, "tradingsymbol");
        var entrySide = NormalizeRequired(body.EntryTransactionType, "entryTransactionType").ToUpperInvariant();
        if (entrySide is not ("BUY" or "SELL"))
            throw new InvalidOperationException("entryTransactionType must be BUY or SELL.");

        var product = NormalizeRequired(body.Product, "product").ToUpperInvariant();
        var stopPct = body.StopLossPercent > 0 ? body.StopLossPercent : 5m;
        var targetPct = body.TriggerPercent > 0 ? body.TriggerPercent : 5m;

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var tickSize = await ResolveKiteTickSizeAsync(exchange, tradingsymbol, apiKey, accessToken, ct).ConfigureAwait(false);

        var reference = NormalizeNullablePrice(body.ReferencePrice);
        var lastPrice = NormalizeNullablePrice(body.LastPrice);
        if (reference is null or <= 0)
        {
            if (lastPrice is null or <= 0)
            {
                var quote = await GetKiteInstrumentLiveQuoteAsync(userId, exchange, tradingsymbol, ct).ConfigureAwait(false);
                lastPrice = quote.LastPrice;
                reference = quote.LastPrice;
            }
            else
            {
                reference = lastPrice;
            }
        }
        else if (lastPrice is null or <= 0)
        {
            lastPrice = reference;
        }

        reference = KiteTickPriceRounding.RoundToTickSize(reference.Value, tickSize);
        lastPrice = KiteTickPriceRounding.RoundToTickSize(lastPrice.Value, tickSize);

        var stopOverride = NormalizeNullablePrice(body.StopLossPrice);
        var targetOverride = NormalizeNullablePrice(body.TriggerPrice);
        if (stopOverride is > 0)
            stopOverride = KiteTickPriceRounding.RoundToTickSize(stopOverride.Value, tickSize);
        if (targetOverride is > 0)
            targetOverride = KiteTickPriceRounding.RoundToTickSize(targetOverride.Value, tickSize);

        var (stopLoss, target, exitSide) = ComputeGttOcoLegPrices(
            entrySide,
            reference.Value,
            stopOverride,
            targetOverride,
            stopPct,
            targetPct,
            tickSize);

        var lossEnabled = body.StopLossEnabled;
        var profitEnabled = body.ProfitEnabled;
        if (!lossEnabled && !profitEnabled)
            throw new InvalidOperationException("Enable at least one GTT leg (stop-loss or profit target).");

        if (lossEnabled && stopLoss <= 0)
            throw new InvalidOperationException("Stop-loss price must be greater than zero.");
        if (profitEnabled && target <= 0)
            throw new InvalidOperationException("Profit target price must be greater than zero.");

        if (lossEnabled && profitEnabled)
        {
            if (entrySide == "BUY" && stopLoss >= target)
                throw new InvalidOperationException("For a BUY entry, stop-loss must be below the target price.");
            if (entrySide == "SELL" && stopLoss <= target)
                throw new InvalidOperationException("For a SELL entry, stop-loss must be above the target price.");

            var lowerTrigger = stopLoss;
            var upperTrigger = target;
            if (entrySide == "SELL")
            {
                lowerTrigger = target;
                upperTrigger = stopLoss;
            }

            var gttRequest = new KiteGttOcoRequest(
                exchange,
                tradingsymbol,
                lastPrice.Value,
                lowerTrigger,
                upperTrigger,
                exitSide,
                body.Quantity,
                product,
                NormalizeOptional(body.Tag));

            var action = await _kiteInstruments.PlaceGttOcoAsync(gttRequest, apiKey, accessToken, ct).ConfigureAwait(false);
            if (!action.Success)
                throw new InvalidOperationException(action.ErrorMessage ?? "Could not place GTT on Kite.");

            return new KiteGttActionResultDto(
                action.TriggerId ?? "unknown",
                "gtt-oco",
                $"GTT OCO placed (SL {stopLoss:0.##}, target {target:0.##}).",
                stopLoss,
                target);
        }

        var singleTrigger = lossEnabled ? stopLoss : target;
        var singleRequest = new KiteGttSingleRequest(
            exchange,
            tradingsymbol,
            lastPrice.Value,
            singleTrigger,
            exitSide,
            body.Quantity,
            product,
            NormalizeOptional(body.Tag));

        var singleAction = await _kiteInstruments.PlaceGttSingleAsync(singleRequest, apiKey, accessToken, ct).ConfigureAwait(false);
        if (!singleAction.Success)
            throw new InvalidOperationException(singleAction.ErrorMessage ?? "Could not place GTT on Kite.");

        if (lossEnabled)
        {
            return new KiteGttActionResultDto(
                singleAction.TriggerId ?? "unknown",
                "gtt-sl",
                $"GTT stop-loss placed at {stopLoss:0.##}.",
                stopLoss,
                0m);
        }

        return new KiteGttActionResultDto(
            singleAction.TriggerId ?? "unknown",
            "gtt-tp",
            $"GTT profit target placed at {target:0.##}.",
            0m,
            target);
    }

    public async Task<KiteOrderActionResultDto> PlaceOrderAsync(
        Guid userId,
        KiteOrderPlaceRequestDto body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new InvalidOperationException("Request body is required.");
        if (body.Quantity < 1)
            throw new InvalidOperationException("quantity must be greater than zero.");

        var provider = await ResolveOrderBrokerAsync(userId, body.Broker, ct).ConfigureAwait(false);
        if (provider == BrokerZerodha)
            return await PlaceKiteOrderAsync(userId, body, ct).ConfigureAwait(false);

        if (provider == BrokerGroww)
        {
            var token = await _brokerSetup.GetBrokerAccessTokenAsync(userId, "Groww", ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Groww is not connected.");

            var segment = NormalizeGrowwSegment(body.Segment, body.Exchange, body.Tradingsymbol);
            var req = new GrowwOrderCreateRequest(
                TradingSymbol: NormalizeRequired(body.Tradingsymbol, "tradingsymbol"),
                Quantity: body.Quantity,
                Price: NormalizeNullablePrice(body.Price),
                TriggerPrice: NormalizeNullablePrice(body.TriggerPrice),
                Validity: NormalizeValidity(body.Validity),
                Exchange: NormalizeRequired(body.Exchange, "exchange").ToUpperInvariant(),
                Segment: segment,
                Product: NormalizeRequired(body.Product, "product").ToUpperInvariant(),
                OrderType: NormalizeRequired(body.OrderType, "orderType").ToUpperInvariant(),
                TransactionType: NormalizeRequired(body.TransactionType, "transactionType").ToUpperInvariant(),
                OrderReferenceId: NormalizeGrowwOrderReference(body.Tag));
            ValidateOrderTypePayload(req.OrderType, req.Price, req.TriggerPrice);

            var action = await _growwTrading.PlaceOrderAsync(req, token, ct).ConfigureAwait(false);
            if (!action.Success)
                throw new InvalidOperationException(action.ErrorMessage ?? "Could not place order on Groww.");
            return new KiteOrderActionResultDto(action.OrderId ?? "unknown", "place", action.Remark ?? "Order placement request accepted.");
        }

        throw new InvalidOperationException($"Unsupported broker provider: {provider}.");
    }

    private async Task<string> ResolveOrderBrokerAsync(Guid userId, string? requestedBroker, CancellationToken ct)
    {
        var providers = await _brokerSetup.GetConnectedBrokerProvidersAsync(userId, ct).ConfigureAwait(false);
        var normalized = providers
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("No connected broker found. Connect Zerodha or Groww first.");

        var requested = string.IsNullOrWhiteSpace(requestedBroker) ? null : requestedBroker.Trim().ToLowerInvariant();
        if (requested is not null)
        {
            if (!normalized.Contains(requested, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Broker '{requested}' is not connected for this user.");
            return requested;
        }

        var status = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(status?.BrokerProvider))
        {
            var active = status.BrokerProvider.Trim().ToLowerInvariant();
            if (normalized.Contains(active, StringComparer.OrdinalIgnoreCase))
                return active;
        }

        return normalized[0]!;
    }

    private static string NormalizeGrowwSegment(string? explicitSegment, string? exchangeRaw, string? tradingsymbol)
    {
        var seg = string.IsNullOrWhiteSpace(explicitSegment) ? null : explicitSegment.Trim().ToUpperInvariant();
        if (seg is "CASH" or "FNO" or "COMMODITY")
            return seg;

        var exchange = string.IsNullOrWhiteSpace(exchangeRaw) ? "" : exchangeRaw.Trim().ToUpperInvariant();
        if (exchange == "MCX")
            return "COMMODITY";
        if (exchange is "NFO" or "BFO")
            return "FNO";

        var ts = string.IsNullOrWhiteSpace(tradingsymbol) ? "" : tradingsymbol.Trim().ToUpperInvariant();
        if (ts.Contains("FUT", StringComparison.Ordinal) || ts.EndsWith("CE", StringComparison.Ordinal) || ts.EndsWith("PE", StringComparison.Ordinal))
            return "FNO";

        return "CASH";
    }

    private static string? NormalizeGrowwOrderReference(string? rawTag)
    {
        var t = NormalizeOptional(rawTag);
        if (t is null)
            return null;

        var safe = new string(t.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (safe.Length < 8)
            return null;
        if (safe.Length > 20)
            safe = safe[..20];
        var hyphenCount = safe.Count(ch => ch == '-');
        return hyphenCount <= 2 ? safe : new string(safe.Where(ch => ch != '-').Take(20).ToArray());
    }

    private static string NormalizeKiteOrderVariety(string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? "regular" : value.Trim().ToLowerInvariant();
        return v is "regular" or "amo" or "co" or "bo" ? v : "regular";
    }

    private static int? NormalizeMarketProtection(string orderTypeRaw, int? requested)
    {
        var orderType = orderTypeRaw.Trim().ToUpperInvariant();
        var isMarketLike = orderType is "MARKET" or "SL-M";

        // Kite now rejects unprotected API MARKET/SL-M orders; default to auto protection.
        if (isMarketLike)
        {
            if (!requested.HasValue || requested.Value == 0)
                return -1;

            if (requested.Value == -1)
                return -1;

            if (requested.Value is >= 1 and <= 100)
                return requested.Value;

            throw new InvalidOperationException("marketProtection must be -1 (auto) or 1..100 for MARKET/SL-M orders.");
        }

        if (!requested.HasValue || requested.Value == 0)
            return null;

        if (requested.Value == -1 || requested.Value is >= 1 and <= 100)
            return requested.Value;

        throw new InvalidOperationException("marketProtection must be 0, -1, or 1..100.");
    }

    private static (decimal StopLoss, decimal Target, string ExitSide) ComputeGttOcoLegPrices(
        string entrySide,
        decimal referencePrice,
        decimal? stopOverride,
        decimal? targetOverride,
        decimal stopLossPercent,
        decimal targetPercent,
        decimal tickSize)
    {
        if (string.Equals(entrySide, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            var stop = stopOverride ?? RoundGttPrice(referencePrice * (1m - stopLossPercent / 100m), tickSize);
            var target = targetOverride ?? RoundGttPrice(referencePrice * (1m + targetPercent / 100m), tickSize);
            return (stop, target, "SELL");
        }

        if (string.Equals(entrySide, "SELL", StringComparison.OrdinalIgnoreCase))
        {
            var stop = stopOverride ?? RoundGttPrice(referencePrice * (1m + stopLossPercent / 100m), tickSize);
            var target = targetOverride ?? RoundGttPrice(referencePrice * (1m - targetPercent / 100m), tickSize);
            return (stop, target, "BUY");
        }

        throw new InvalidOperationException("entryTransactionType must be BUY or SELL.");
    }

    private static decimal RoundGttPrice(decimal value, decimal tickSize) =>
        KiteTickPriceRounding.RoundToTickSize(value, tickSize);

    private async Task<decimal> ResolveKiteTickSizeAsync(
        string exchange,
        string tradingsymbol,
        string apiKey,
        string accessToken,
        CancellationToken ct)
    {
        var fetched = await _kiteInstruments
            .FetchInstrumentRowByTradingsymbolAsync(exchange, tradingsymbol, apiKey, accessToken, ct)
            .ConfigureAwait(false);
        if (fetched.Success
            && fetched.Items.Count > 0
            && fetched.Items[0].TickSize is decimal tick
            && tick > 0)
        {
            return tick;
        }

        throw new InvalidOperationException(
            $"Could not resolve tick size for {exchange}:{tradingsymbol}. GTT prices must be multiples of the instrument tick size.");
    }
}
