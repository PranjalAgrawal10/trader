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

public sealed class BrokerService : IBrokerService
{
    private const string BrokerZerodha = "zerodha";
    private const string BrokerGroww = "groww";
    private const int ChartZoomMinBars = 1;
    private const int ChartZoomMaxBars = 500_000;
    private const int FavoriteMlThrottleMinMinutes = 1;
    private const int FavoriteMlThrottleMaxMinutes = 1440;
    private const int FavoriteMlMinSecondsAfterBarOpenMax = 86_400;
    private static readonly JsonSerializerOptions ChartZoomJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string PendingStateCachePrefix = "Trader.KiteOAuth.PendingState:";
    private static readonly TimeSpan PendingStateTtl = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Rows returned to the SPA Browse panels (F&amp;O merged NFO+BFO, MCX) after sorting by nearest expiry.
    /// </summary>
    private const int KiteInstrumentBrowsePanelMaxRows = 50;

    /// <summary>
    /// Per-exchange CSV streaming cap when building Browse lists. Larger than <see cref="KiteInstrumentBrowsePanelMaxRows"/> so rows can be sorted by expiry (CSV order is arbitrary); callers still receive at most <see cref="KiteInstrumentBrowsePanelMaxRows"/> after merge/sort.
    /// </summary>
    private const int KiteInstrumentBrowseExchangeFetchRows = 4000;

    /// <summary>
    /// Sentinel for "return every match" when the user runs a search from the UI — the
    /// preview-list cap (<see cref="KiteInstrumentBrowsePanelMaxRows"/>) only applies to
    /// the Browse-tab preview lists, not to explicit search results.
    /// </summary>
    private const int KiteInstrumentSearchUnlimited = int.MaxValue;
    private const int KiteInstrumentSearchQueryMaxLength = 128;

    private const int MaxKiteFavoriteInstrumentsPerUser = 400;
    private const int MaxKiteTradingLockInstrumentsPerUser = 400;

    private const int KiteQuoteOhlcBatchSizeBroker = 140;

    /// <summary>Shared server-side cache TTL for trimmed chart composites (avoid duplicate slow Kite calls when SPA parallel-fetches).</summary>
    private static readonly TimeSpan ChartHistoricalCacheTtl = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan LiveQuoteCacheTtl = TimeSpan.FromSeconds(5);
    private const int KiteTodayTopPerformersMaxTake = 30;

    private readonly IBrokerSetupGateway _brokerSetup;
    private readonly IKiteOAuthStateCodec _stateCodec;
    private readonly IKiteSessionExchange _kiteSessionExchange;
    private readonly IKiteInstrumentsClient _kiteInstruments;
    private readonly IGrowwTradingClient _growwTrading;
    private readonly IKiteFavoriteInstrumentRepository _kiteFavoriteInstruments;
    private readonly IKiteTradingLockInstrumentRepository _kiteTradingLocks;
    private readonly IKiteInstrumentsChartSettingsGateway _kiteChartSettings;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly IMemoryCache _memoryCache;
    private readonly IWalletService _wallet;
    private readonly IUserRepository _users;
    private readonly IDemoPaperPositionRepository _demoPaperPositions;
    private readonly IDemoPaperBuyLegRepository _demoPaperBuyLegs;
    private readonly IDemoPaperTradeLogRepository _demoPaperTradeLogs;

    public BrokerService(
        IBrokerSetupGateway brokerSetup,
        IKiteOAuthStateCodec stateCodec,
        IKiteSessionExchange kiteSessionExchange,
        IKiteInstrumentsClient kiteInstruments,
        IGrowwTradingClient growwTrading,
        IKiteFavoriteInstrumentRepository kiteFavoriteInstruments,
        IKiteTradingLockInstrumentRepository kiteTradingLocks,
        IKiteInstrumentsChartSettingsGateway kiteChartSettings,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        IMemoryCache memoryCache,
        IWalletService wallet,
        IUserRepository users,
        IDemoPaperPositionRepository demoPaperPositions,
        IDemoPaperBuyLegRepository demoPaperBuyLegs,
        IDemoPaperTradeLogRepository demoPaperTradeLogs)
    {
        _brokerSetup = brokerSetup;
        _stateCodec = stateCodec;
        _kiteSessionExchange = kiteSessionExchange;
        _kiteInstruments = kiteInstruments;
        _growwTrading = growwTrading;
        _kiteFavoriteInstruments = kiteFavoriteInstruments;
        _kiteTradingLocks = kiteTradingLocks;
        _kiteChartSettings = kiteChartSettings;
        _kiteOptions = kiteOptions;
        _memoryCache = memoryCache;
        _wallet = wallet;
        _users = users;
        _demoPaperPositions = demoPaperPositions;
        _demoPaperBuyLegs = demoPaperBuyLegs;
        _demoPaperTradeLogs = demoPaperTradeLogs;
    }

    public async Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");

        var provider = snapshot.BrokerProvider;
        var at = snapshot.BrokerConnectedAt;
        return new BrokerStatusDto(!string.IsNullOrEmpty(provider), at, provider);
    }

    public async Task<IReadOnlyList<BrokerProviderAvailabilityDto>> GetOrderBrokerProvidersAsync(Guid userId, CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var connected = await _brokerSetup.GetConnectedBrokerProvidersAsync(userId, ct).ConfigureAwait(false);
        var set = new HashSet<string>(connected.Select(x => x.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
        return new[]
        {
            new BrokerProviderAvailabilityDto(BrokerZerodha, "Zerodha Kite", set.Contains(BrokerZerodha)),
            new BrokerProviderAvailabilityDto(BrokerGroww, "Groww", set.Contains(BrokerGroww)),
        };
    }

    public Task CompleteSetupAsync(Guid userId, CancellationToken ct = default) =>
        _brokerSetup.CompleteBrokerSetupAsync(userId, ct);

    public Task<KiteLoginUrlBuildResult> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default)
    {
        _ = ct;
        var opt = _kiteOptions.Value;
        if (string.IsNullOrWhiteSpace(opt.ApiKey) || string.IsNullOrWhiteSpace(opt.RedirectUrl))
        {
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variables ZerodhaKite__ApiKey and ZerodhaKite__RedirectUrl (or use .env.development in Development; see README).");
        }

        var fullState = _stateCodec.Encode(userId);
        var shortKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _memoryCache.Set(PendingStateCachePrefix + shortKey, fullState, PendingStateTtl);

        // redirect_params: Kite appends these to the redirect URL (see kite.trade docs). Backup if `state` is omitted.
        var redirectParams = Uri.EscapeDataString($"trader_oauth={shortKey}");
        var url =
            $"https://kite.zerodha.com/connect/login?v=3&api_key={Uri.EscapeDataString(opt.ApiKey)}&state={Uri.EscapeDataString(shortKey)}&redirect_params={redirectParams}";
        return Task.FromResult(new KiteLoginUrlBuildResult(url, shortKey));
    }

    public async Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default)
    {
        var (signedState, pendingKey) = ResolveKiteOAuthState(state);
        var userId = _stateCodec.TryDecode(signedState)
                     ?? throw new InvalidOperationException("Invalid or expired OAuth state. Try connecting again.");

        var exchanged = await _kiteSessionExchange.ExchangeAsync(requestToken, ct);
        if (!exchanged.Success || string.IsNullOrEmpty(exchanged.AccessToken) || string.IsNullOrEmpty(exchanged.KiteUserId))
        {
            throw new InvalidOperationException(exchanged.ErrorMessage ?? "Could not complete Kite login.");
        }

        await _brokerSetup.PersistKiteSessionAsync(
            userId,
            new BrokerKitePersistRequest(exchanged.AccessToken, exchanged.RefreshToken, exchanged.KiteUserId),
            ct);

        if (pendingKey is not null)
            _memoryCache.Remove(PendingStateCachePrefix + pendingKey);

        return await GetStatusAsync(userId, ct);
    }

    public async Task<BrokerStatusDto> ConnectGrowwAsync(Guid userId, GrowwConnectRequestDto body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        var accessToken = NormalizeOptional(body.AccessToken);
        var apiKey = NormalizeOptional(body.ApiKey);
        var apiSecret = NormalizeOptional(body.ApiSecret);
        var totp = NormalizeOptional(body.Totp);

        GrowwTokenAccessResult? generated = null;
        if (accessToken is null)
        {
            if (apiKey is null)
                throw new InvalidOperationException("Provide accessToken, or provide apiKey with apiSecret/totp.");

            if (apiSecret is not null)
            {
                generated = await _growwTrading.CreateAccessTokenByApprovalAsync(apiKey, apiSecret, ct).ConfigureAwait(false);
            }
            else if (totp is not null)
            {
                generated = await _growwTrading.CreateAccessTokenByTotpAsync(apiKey, totp, ct).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("apiSecret or totp is required when accessToken is not provided.");
            }

            if (!generated.Success || string.IsNullOrWhiteSpace(generated.AccessToken))
                throw new InvalidOperationException(generated.ErrorMessage ?? "Could not create Groww access token.");

            accessToken = generated.AccessToken.Trim();
        }

        await _brokerSetup.PersistGrowwSessionAsync(
            userId,
            new BrokerGrowwPersistRequest(
                accessToken,
                generated?.ExpiresAt,
                apiKey),
            ct).ConfigureAwait(false);

        return await GetStatusAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task<BrokerStatusDto> DisconnectAsync(Guid userId, string? broker = null, CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(broker) ? null : broker.Trim();
        await _brokerSetup.DisconnectBrokerAsync(userId, normalized, ct);
        return await GetStatusAsync(userId, ct);
    }

    public async Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        var fetchCap = (int?)KiteInstrumentBrowseExchangeFetchRows;
        var nfoTask = _kiteInstruments.FetchExchangeInstrumentsAsync("NFO", apiKey, accessToken, fetchCap, ct);
        var bfoTask = _kiteInstruments.FetchExchangeInstrumentsAsync("BFO", apiKey, accessToken, fetchCap, ct);
        var mcxTask = _kiteInstruments.FetchExchangeInstrumentsAsync("MCX", apiKey, accessToken, fetchCap, ct);

        await Task.WhenAll(nfoTask, bfoTask, mcxTask).ConfigureAwait(false);

        var nfo = await nfoTask.ConfigureAwait(false);
        if (!nfo.Success)
            throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not load NFO instruments from Kite.");

        var fno = new List<KiteInstrumentListItemDto>(nfo.Items);
        var fnoTruncated = nfo.Truncated;

        var bfo = await bfoTask.ConfigureAwait(false);
        if (bfo.Success)
        {
            fno.AddRange(bfo.Items);
            fnoTruncated |= bfo.Truncated;
        }

        fno.Sort(static (a, b) => CompareInstrumentExpiryAscending(a, b));

        var combinedFnoCount = fno.Count;
        if (combinedFnoCount > KiteInstrumentBrowsePanelMaxRows)
        {
            fno = fno.Take(KiteInstrumentBrowsePanelMaxRows).ToList();
            fnoTruncated = true;
        }

        var mcx = await mcxTask.ConfigureAwait(false);
        if (!mcx.Success)
            throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not load MCX instruments from Kite.");

        var commodities = new List<KiteInstrumentListItemDto>(mcx.Items);
        var commoditiesTruncated = mcx.Truncated;
        commodities.Sort(static (a, b) => CompareInstrumentExpiryAscending(a, b));
        if (commodities.Count > KiteInstrumentBrowsePanelMaxRows)
        {
            commodities = commodities.Take(KiteInstrumentBrowsePanelMaxRows).ToList();
            commoditiesTruncated = true;
        }

        return new KiteFnoCommodityListsDto(fno, commodities, fnoTruncated, commoditiesTruncated);
    }

    /// <inheritdoc />
    public async Task<KiteTodayTopPerformersDto> GetKiteTodayTopPerformersAsync(Guid userId, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, KiteTodayTopPerformersMaxTake);

        static string QuoteKey(KiteInstrumentListItemDto x) =>
            $"{x.Exchange.Trim()}:{x.Tradingsymbol.Trim()}";

        var lists = await GetKiteFnoCommodityInstrumentsAsync(userId, ct).ConfigureAwait(false);

        var uniqueRows = lists.Fno
            .Concat(lists.Commodities)
            .GroupBy(QuoteKey, StringComparer.Ordinal)
            .Select(static g => g.First())
            .ToList();

        var quoteKeys = uniqueRows.Select(QuoteKey).ToList();

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        var quotes = new Dictionary<string, KiteQuoteOhlcTickDto>(StringComparer.Ordinal);
        for (var off = 0; off < quoteKeys.Count; off += KiteQuoteOhlcBatchSizeBroker)
        {
            var batchCount = Math.Min(KiteQuoteOhlcBatchSizeBroker, quoteKeys.Count - off);
            var slice = quoteKeys.Skip(off).Take(batchCount).ToList();
            var fetch = await _kiteInstruments.FetchQuoteOhlcAsync(slice, apiKey, accessToken, ct).ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load OHLC quotes from Kite.");

            foreach (var kv in fetch.ByKey)
                quotes[kv.Key] = kv.Value;
        }

        var movers = new List<KiteInstrumentMoverDto>(uniqueRows.Count);
        foreach (var r in uniqueRows)
        {
            if (!quotes.TryGetValue(QuoteKey(r), out var q))
                continue;

            var prevClose = q.OhlcClose;
            if (prevClose <= 0)
                continue;

            var pct = (q.LastPrice - prevClose) / prevClose * 100m;
            movers.Add(new KiteInstrumentMoverDto(r, q.LastPrice, prevClose, pct));
        }

        var items = movers
            .OrderByDescending(static m => m.ChangePercent)
            .Take(take)
            .ToList();

        return new KiteTodayTopPerformersDto(
            items,
            "% vs previous session close (Kite OHLC); universe = same capped preview as Browse lists.");
    }

    public async Task<KiteInstrumentSearchDto> SearchKiteInstrumentsAsync(
        Guid userId,
        string query,
        KiteInstrumentSearchSegment segment,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Search text is required.");

        var needle = query.Trim();
        if (needle.Length > KiteInstrumentSearchQueryMaxLength)
            throw new InvalidOperationException($"Search text must be at most {KiteInstrumentSearchQueryMaxLength} characters.");

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        if (segment == KiteInstrumentSearchSegment.All)
        {
            var fTask = SearchKiteInstrumentsAsync(userId, query, KiteInstrumentSearchSegment.Fno, ct);
            var mTask = SearchKiteInstrumentsAsync(userId, query, KiteInstrumentSearchSegment.Mcx, ct);
            var sTask = SearchKiteInstrumentsAsync(userId, query, KiteInstrumentSearchSegment.Spot, ct);
            await Task.WhenAll(fTask, mTask, sTask).ConfigureAwait(false);
            var f = await fTask.ConfigureAwait(false);
            var m = await mTask.ConfigureAwait(false);
            var s = await sTask.ConfigureAwait(false);

            var scanTruncated = f.ScanTruncated || m.ScanTruncated || s.ScanTruncated;
            var byKey = new Dictionary<string, KiteInstrumentListItemDto>(StringComparer.Ordinal);
            void AddDistinct(IReadOnlyList<KiteInstrumentListItemDto> items)
            {
                foreach (var item in items)
                {
                    var k = $"{item.Exchange.Trim()}:{item.Tradingsymbol.Trim()}";
                    if (!byKey.ContainsKey(k))
                        byKey[k] = item;
                }
            }

            AddDistinct(f.Items);
            AddDistinct(m.Items);
            AddDistinct(s.Items);
            return new KiteInstrumentSearchDto(byKey.Values.ToList(), scanTruncated);
        }

        if (segment == KiteInstrumentSearchSegment.Fno)
        {
            var nfoTask = _kiteInstruments.SearchExchangeInstrumentsAsync(
                "NFO", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, ct: ct);
            var bfoTask = _kiteInstruments.SearchExchangeInstrumentsAsync(
                "BFO", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, ct: ct);
            await Task.WhenAll(nfoTask, bfoTask).ConfigureAwait(false);

            var nfo = await nfoTask.ConfigureAwait(false);
            if (!nfo.Success)
                throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not search NFO instruments on Kite.");

            var combined = new List<KiteInstrumentListItemDto>(nfo.Items);
            var scanTruncated = nfo.Truncated;

            var bfo = await bfoTask.ConfigureAwait(false);
            if (bfo.Success)
            {
                combined.AddRange(bfo.Items);
                scanTruncated |= bfo.Truncated;
            }

            return new KiteInstrumentSearchDto(combined, scanTruncated);
        }

        if (segment == KiteInstrumentSearchSegment.Spot)
        {
            var nseTask = _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "NSE", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, equityCashOnly: true, ct);
            var bseTask = _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "BSE", apiKey, accessToken, needle, KiteInstrumentSearchUnlimited, equityCashOnly: true, ct);
            await Task.WhenAll(nseTask, bseTask).ConfigureAwait(false);

            var nse = await nseTask.ConfigureAwait(false);
            if (!nse.Success)
                throw new InvalidOperationException(nse.ErrorMessage ?? "Could not search NSE equity instruments on Kite.");

            var combined = new List<KiteInstrumentListItemDto>(nse.Items);
            var scanTruncated = nse.Truncated;

            var bse = await bseTask.ConfigureAwait(false);
            if (!bse.Success)
                throw new InvalidOperationException(bse.ErrorMessage ?? "Could not search BSE equity instruments on Kite.");

            combined.AddRange(bse.Items);
            scanTruncated |= bse.Truncated;

            return new KiteInstrumentSearchDto(combined, scanTruncated);
        }

        if (segment == KiteInstrumentSearchSegment.Mcx)
        {
            var mcx = await _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "MCX",
                    apiKey,
                    accessToken,
                    needle,
                    KiteInstrumentSearchUnlimited,
                    ct: ct)
                .ConfigureAwait(false);
            if (!mcx.Success)
                throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not search MCX instruments on Kite.");

            return new KiteInstrumentSearchDto(mcx.Items, mcx.Truncated);
        }

        throw new InvalidOperationException($"Unexpected segment: {segment}.");
    }

    public async Task<KiteHistoricalCandlesDto> GetKiteHistoricalCandlesAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        return await GetOrComposeChartHistoricalCandlesCachedAsync(userId, instrumentToken, interval, fromUtc, toUtc, ct)
            .ConfigureAwait(false);
    }

    public async Task<KiteHistoricalOhlcvOnlyDto> GetKiteHistoricalChartOhlcvAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        var full = await GetOrComposeChartHistoricalCandlesCachedAsync(userId, instrumentToken, interval, fromUtc, toUtc, ct)
            .ConfigureAwait(false);
        return ProjectToOhlcvOnly(full);
    }

    public async Task<KiteHistoricalOverlaysDto> GetKiteHistoricalChartOverlaysAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        var full = await GetOrComposeChartHistoricalCandlesCachedAsync(userId, instrumentToken, interval, fromUtc, toUtc, ct)
            .ConfigureAwait(false);
        return ProjectToOverlays(full);
    }

    public async Task<KiteInstrumentLiveQuoteDto> GetKiteInstrumentLiveQuoteAsync(
        Guid userId,
        string exchange,
        string tradingsymbol,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(tradingsymbol))
            throw new InvalidOperationException("exchange and tradingsymbol are required.");

        var ex = exchange.Trim().ToUpperInvariant();
        var ts = tradingsymbol.Trim();
        var iq = $"{ex}:{ts}";
        var cacheKey = $"Trader.LiveQuote:v1:{userId:N}:{ex}:{ts}";
        if (_memoryCache.TryGetValue(cacheKey, out KiteInstrumentLiveQuoteDto? cachedQuote) && cachedQuote is not null)
            return cachedQuote;

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var fetch = await _kiteInstruments.FetchQuoteOhlcAsync([iq], apiKey, accessToken, ct).ConfigureAwait(false);
        if (!fetch.Success || fetch.ByKey is null || !fetch.ByKey.TryGetValue(iq, out var tick))
        {
            throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load live quote.");
        }

        var dto = new KiteInstrumentLiveQuoteDto(ex, ts, tick.LastPrice, tick.OhlcClose);
        _memoryCache.Set(
            cacheKey,
            dto,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = LiveQuoteCacheTtl });
        return dto;
    }

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

    /// <summary>Validated composite; cache hit skips Kite + session churn when parallel chart routes share the window.</summary>
    private async Task<KiteHistoricalCandlesDto> GetOrComposeChartHistoricalCandlesCachedAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken) || !instrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token from Kite is required.");

        var token = instrumentToken.Trim();
        var code = NormalizeUiChartInterval(interval);
        var requestEnd = toUtc ?? DateTimeOffset.UtcNow;
        var requestStart = fromUtc ?? requestEnd - DefaultChartLookback(code);
        if (requestStart >= requestEnd)
            throw new InvalidOperationException("Start time must be before end time.");

        var fetchStart = ComputeMaWarmupFetchStart(requestStart, code);
        var cacheKey = $"Trader.ChartHist:v2:{userId:N}:{token}:{code}:{fetchStart.UtcTicks}:{requestEnd.UtcTicks}";
        if (_memoryCache.TryGetValue(cacheKey, out KiteHistoricalCandlesDto? hit) && hit is not null)
            return hit;

        var dto = await FetchHistoricalCandlesFreshAsync(userId, token, code, requestStart, requestEnd, fetchStart, ct)
            .ConfigureAwait(false);
        _memoryCache.Set(
            cacheKey,
            dto,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChartHistoricalCacheTtl });
        return dto;
    }

    private async Task<KiteHistoricalCandlesDto> FetchHistoricalCandlesFreshAsync(
        Guid userId,
        string token,
        string code,
        DateTimeOffset requestStart,
        DateTimeOffset requestEnd,
        DateTimeOffset fetchStart,
        CancellationToken ct)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        if (code is "2m" or "4m")
        {
            var period = code == "2m" ? 2 : 4;
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "minute", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeMinuteCandles(fetch.Candles, period);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        if (code is "2h" or "4h" or "8h")
        {
            var bucketHours = code switch
            {
                "2h" => 2L,
                "4h" => 4L,
                "8h" => 8L,
                _ => 4L,
            };
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "60minute", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeOhlcByBucketSeconds(fetch.Candles, bucketHours * 3600);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        if (code == "90m")
        {
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "minute", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeOhlcByBucketSeconds(fetch.Candles, 90L * 60);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        if (code == "1w")
        {
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "day", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeEveryNCandles(fetch.Candles, 7);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        var kiteInterval = MapUiIntervalToKite(code);
        var raw = await _kiteInstruments
            .FetchHistoricalCandlesAsync(token, kiteInterval, apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
            .ConfigureAwait(false);
        if (!raw.Success)
            throw new InvalidOperationException(raw.ErrorMessage ?? "Could not load candles from Kite.");

        return FinalizeChartHistoricalCandles(raw.Candles, code, requestStart, requestEnd);
    }

    private static KiteHistoricalOhlcvOnlyDto ProjectToOhlcvOnly(KiteHistoricalCandlesDto dto)
    {
        var candles = dto.Candles
            .Select(c => new KiteHistoricalOhlcvOnlyCandleDto(c.Time, c.Open, c.High, c.Low, c.Close, c.Volume))
            .ToList();
        return new KiteHistoricalOhlcvOnlyDto(candles, dto.Interval, dto.From, dto.To);
    }

    private static KiteHistoricalOverlaysDto ProjectToOverlays(KiteHistoricalCandlesDto dto)
    {
        var pts = dto.Candles
            .Select(c => new KiteHistoricalOverlayPointDto(
                c.Time,
                c.Sma20,
                c.Ema9,
                c.Ema21,
                c.SrSupport,
                c.SrResistance))
            .ToList();
        return new KiteHistoricalOverlaysDto(pts, dto.Interval, dto.From, dto.To);
    }

    public async Task<KiteFavoriteInstrumentsListDto> GetKiteFavoriteInstrumentsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var rows = await _kiteFavoriteInstruments.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var items = rows.Select(MapFavoriteToDto).ToList();
        return new KiteFavoriteInstrumentsListDto(items);
    }

    public async Task AddKiteFavoriteInstrumentAsync(
        Guid userId,
        KiteInstrumentListItemDto item,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(item.InstrumentToken)
            || !item.InstrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token from Kite is required.");

        var token = item.InstrumentToken.Trim();
        if (token.Length > 64)
            throw new InvalidOperationException("Instrument token is too long.");

        if (string.IsNullOrWhiteSpace(item.Tradingsymbol) || string.IsNullOrWhiteSpace(item.Exchange))
            throw new InvalidOperationException("Tradingsymbol and exchange are required.");

        var existing = await _kiteFavoriteInstruments.FindAsync(userId, token, ct).ConfigureAwait(false);
        if (existing is not null)
            return;

        var count = await _kiteFavoriteInstruments.CountByUserAsync(userId, ct).ConfigureAwait(false);
        if (count >= MaxKiteFavoriteInstrumentsPerUser)
            throw new InvalidOperationException($"You can save at most {MaxKiteFavoriteInstrumentsPerUser} favorite instruments.");

        var entity = new KiteFavoriteInstrument
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentToken = token,
            Tradingsymbol = item.Tradingsymbol.Trim(),
            Exchange = item.Exchange.Trim(),
            Name = NullableNorm(item.Name),
            InstrumentType = NullableNorm(item.InstrumentType),
            Segment = NullableNorm(item.Segment),
            Expiry = NullableNorm(item.Expiry),
            Strike = item.Strike,
            LotSize = item.LotSize,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await _kiteFavoriteInstruments.AddAsync(entity, ct).ConfigureAwait(false);
        await _kiteFavoriteInstruments.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveKiteFavoriteInstrumentAsync(Guid userId, string instrumentToken, CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(instrumentToken))
            return;

        var token = instrumentToken.Trim();
        var existing = await _kiteFavoriteInstruments.FindAsync(userId, token, ct).ConfigureAwait(false);
        if (existing is null)
            return;

        _kiteFavoriteInstruments.Remove(existing);
        await _kiteFavoriteInstruments.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<KiteTradingLocksListDto> GetKiteTradingLocksAsync(Guid userId, CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var rows = await _kiteTradingLocks.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var items = rows.Select(MapTradingLockToDto).ToList();
        return new KiteTradingLocksListDto(items);
    }

    public async Task AddKiteTradingLockAsync(
        Guid userId,
        KiteInstrumentListItemDto item,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(item.InstrumentToken)
            || !item.InstrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token from Kite is required.");

        var token = item.InstrumentToken.Trim();
        if (token.Length > 64)
            throw new InvalidOperationException("Instrument token is too long.");

        if (string.IsNullOrWhiteSpace(item.Tradingsymbol) || string.IsNullOrWhiteSpace(item.Exchange))
            throw new InvalidOperationException("Tradingsymbol and exchange are required.");

        var existing = await _kiteTradingLocks.FindAsync(userId, token, ct).ConfigureAwait(false);
        if (existing is not null)
            return;

        var count = await _kiteTradingLocks.CountByUserAsync(userId, ct).ConfigureAwait(false);
        if (count >= MaxKiteTradingLockInstrumentsPerUser)
            throw new InvalidOperationException($"You can lock at most {MaxKiteTradingLockInstrumentsPerUser} instruments for trading.");

        var entity = new KiteTradingLockInstrument
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentToken = token,
            Tradingsymbol = item.Tradingsymbol.Trim(),
            Exchange = item.Exchange.Trim(),
            Name = NullableNorm(item.Name),
            InstrumentType = NullableNorm(item.InstrumentType),
            Segment = NullableNorm(item.Segment),
            Expiry = NullableNorm(item.Expiry),
            Strike = item.Strike,
            LotSize = item.LotSize,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await _kiteTradingLocks.AddAsync(entity, ct).ConfigureAwait(false);
        await _kiteTradingLocks.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveKiteTradingLockAsync(Guid userId, string instrumentToken, CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(instrumentToken))
            return;

        var token = instrumentToken.Trim();
        var existing = await _kiteTradingLocks.FindAsync(userId, token, ct).ConfigureAwait(false);
        if (existing is null)
            return;

        _kiteTradingLocks.Remove(existing);
        await _kiteTradingLocks.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<KiteInstrumentsChartSettingsDto> GetKiteInstrumentsChartSettingsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var row = await _kiteChartSettings.GetAsync(userId, ct).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("User not found.");

        var wallet = await _wallet.GetBalanceAsync(userId, ct).ConfigureAwait(false);
        var notional = decimal.Round(Math.Max(0m, wallet.Balance), 2, MidpointRounding.AwayFromZero);

        return new KiteInstrumentsChartSettingsDto(
            row.Interval,
            row.RangePreset,
            row.GraphType,
            ParseChartZoomMap(row.ChartZoomByInstrumentTokenJson),
            ParseChartIntervalOverrideMap(row.ChartIntervalByInstrumentTokenJson),
            row.FavoriteMlAutomationEnabled ?? false,
            row.FavoriteMlAutomationInterval,
            ThrottleSecondsToApiMinutes(row.FavoriteMlAutomationPollIntervalSeconds),
            row.FavoriteMlAutomationMinSecondsAfterBarOpen,
            ParseTrendAnalysisIntervalsFromJson(row.TrendAnalysisIntervalsJson),
            row.DemoAutoTradeEnabled ?? false,
            notional,
            DemoAutoTradeStrategyIds.NormalizeOrDefault(row.DemoAutoTradeStrategy));
    }

    public async Task<ScalperSettingsDto> GetScalperSettingsAsync(Guid userId, CancellationToken ct = default)
    {
        var row = await _kiteChartSettings.GetScalperSettingsAsync(userId, ct).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("User not found.");

        var interval = NormalizeScalperIntervalOrDefault(row.Interval);
        var rangePreset = NormalizeScalperRangePresetOrDefault(row.RangePreset);
        var graphType = NormalizeScalperGraphTypeOrDefault(row.GraphType);
        var stopPts = NormalizeScalperPointsOrDefault(row.SafeStopLossPoints, 10m);
        var triggerPts = NormalizeScalperPointsOrDefault(row.SafeTriggerPoints, 20m);
        return new ScalperSettingsDto(
            interval,
            rangePreset,
            graphType,
            row.ShowVolume,
            row.SafeModeEnabled,
            stopPts,
            triggerPts);
    }

    public async Task SaveScalperSettingsAsync(Guid userId, ScalperSettingsPutDto body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var interval = NormalizeScalperIntervalOrDefault(body.Interval);
        var rangePreset = NormalizeScalperRangePresetOrDefault(body.RangePreset);
        var graphType = NormalizeScalperGraphTypeOrDefault(body.GraphType);
        var stopPts = NormalizeScalperPointsOrDefault(body.SafeStopLossPoints, 10m);
        var triggerPts = NormalizeScalperPointsOrDefault(body.SafeTriggerPoints, 20m);

        await _kiteChartSettings.SaveScalperSettingsAsync(
            userId,
            new ScalperSettingsState(
                interval,
                rangePreset,
                graphType,
                body.ShowVolume,
                body.SafeModeEnabled,
                stopPts,
                triggerPts),
            ct).ConfigureAwait(false);
    }

    public async Task SetDemoAutoTradePreferencesAsync(
        Guid userId,
        bool enabled,
        string? strategyRaw,
        CancellationToken ct = default)
    {
        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(strategyRaw))
            normalized = DemoAutoTradeStrategyIds.ParseRequired(strategyRaw);

        await _kiteChartSettings.SetDemoAutoTradePreferencesAsync(userId, enabled, normalized, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemoPaperPositionListItemDto>> GetDemoPaperPositionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var positions = await _demoPaperPositions.ListByUserAsync(userId, ct).ConfigureAwait(false);
        if (positions.Count == 0)
            return Array.Empty<DemoPaperPositionListItemDto>();

        var locks = await _kiteTradingLocks.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var byTok = locks.ToDictionary(x => x.InstrumentToken, StringComparer.Ordinal);
        var legs = await _demoPaperBuyLegs.ListOpenByUserAsync(userId, ct).ConfigureAwait(false);
        var openBuysByToken = legs
            .GroupBy(l => l.InstrumentToken, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyList<DemoPaperOpenBuyMarkerDto>)g.OrderBy(l => l.BoughtAtUtc)
                        .Select(l => new DemoPaperOpenBuyMarkerDto(l.BoughtAtUtc, l.ContractsRemaining))
                        .ToList(),
                StringComparer.Ordinal);

        var positionTokens = positions.Select(x => x.InstrumentToken).Distinct(StringComparer.Ordinal).ToArray();
        var lastBuyByToken = await _demoPaperTradeLogs
            .GetLatestBuyLastPriceByInstrumentTokensAsync(userId, positionTokens, ct)
            .ConfigureAwait(false);

        var list = new List<DemoPaperPositionListItemDto>(positions.Count);
        foreach (var p in positions)
        {
            byTok.TryGetValue(p.InstrumentToken, out var lk);
            openBuysByToken.TryGetValue(p.InstrumentToken, out var openBuys);
            decimal? lastBuyPrice = null;
            if (p.OpenContracts > 0 && lastBuyByToken.TryGetValue(p.InstrumentToken, out var lastBuyPx))
                lastBuyPrice = lastBuyPx;
            list.Add(
                new DemoPaperPositionListItemDto(
                    p.InstrumentToken,
                    lk?.Tradingsymbol ?? p.InstrumentToken,
                    lk?.Exchange ?? "—",
                    lk?.LotSize,
                    p.OpenContracts,
                    openBuys ?? Array.Empty<DemoPaperOpenBuyMarkerDto>(),
                    lastBuyPrice));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemoPaperTradeHistoryRowDto>> GetDemoPaperTradeHistoryAsync(
        Guid userId,
        int? take = null,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var n = Math.Clamp(take ?? 500, 1, 2000);
        var rows = await _demoPaperTradeLogs.ListRecentByUserAsync(userId, n, ct).ConfigureAwait(false);
        var list = new List<DemoPaperTradeHistoryRowDto>(rows.Count);
        foreach (var r in rows)
        {
            list.Add(
                new DemoPaperTradeHistoryRowDto(
                    r.Id,
                    r.ExecutedAtUtc,
                    r.InstrumentToken,
                    r.Tradingsymbol,
                    r.Exchange,
                    r.Side,
                    r.Contracts,
                    r.LastPrice,
                    r.LotSize,
                    r.CashFlowInr,
                    r.WalletBalanceAfter,
                    r.OpenContractsAfter));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<DemoPaperTradeResultDto> ExecuteDemoPaperTradeAsync(
        Guid userId,
        DemoPaperTradeRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = (request.InstrumentToken ?? string.Empty).Trim();
        if (token.Length == 0 || !token.All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token is required.");

        var side = (request.Side ?? string.Empty).Trim().ToLowerInvariant();
        if (side is not ("buy" or "sell"))
            throw new InvalidOperationException("Side must be buy or sell.");

        if (request.Contracts < 1 || request.Contracts > 1_000_000)
            throw new InvalidOperationException("Lots must be between 1 and 1,000,000.");

        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);

        var lockRow = await _kiteTradingLocks.FindAsync(userId, token, ct).ConfigureAwait(false);
        if (lockRow is null)
            throw new InvalidOperationException("Instrument must be in Locked for trading to paper trade.");

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var instrumentRowFetch = await _kiteInstruments
            .FetchInstrumentRowByTokenAsync(lockRow.Exchange, token, apiKey, accessToken, ct)
            .ConfigureAwait(false);

        int lotMult;
        if (instrumentRowFetch.Success
            && instrumentRowFetch.Items.Count == 1
            && instrumentRowFetch.Items[0].LotSize is int kiteLot
            && kiteLot >= 1)
        {
            lotMult = kiteLot;
            if (lockRow.LotSize != lotMult)
                lockRow.LotSize = lotMult;
        }
        else if (lockRow.LotSize is int lockedLot && lockedLot >= 1)
        {
            lotMult = lockedLot;
        }
        else
        {
            var hint = instrumentRowFetch.ErrorMessage ?? "instrument row was not returned.";
            throw new InvalidOperationException(
                $"Could not resolve exchange lot size ({hint}). Remove the lock and add the symbol again from search.");
        }

        var lots = request.Contracts;

        var quote = await GetKiteInstrumentLiveQuoteAsync(userId, lockRow.Exchange, lockRow.Tradingsymbol, ct)
            .ConfigureAwait(false);

        var ltp = quote.LastPrice;
        if (ltp <= 0)
            throw new InvalidOperationException("Could not read a positive last price for this instrument.");

        var legCash = decimal.Round(
            decimal.Round(ltp * lotMult, 2, MidpointRounding.AwayFromZero) * lots,
            2,
            MidpointRounding.AwayFromZero);

        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        DemoPaperPosition? pos = await _demoPaperPositions.FindByUserAndTokenAsync(userId, token, ct)
            .ConfigureAwait(false);

        decimal cashFlow;
        int openAfter;

        if (side == "buy")
        {
            if (user.WalletBalance < legCash)
                throw new InvalidOperationException("Insufficient wallet balance for this paper buy.");

            user.WalletBalance = decimal.Round(user.WalletBalance - legCash, 2, MidpointRounding.AwayFromZero);
            cashFlow = -legCash;

            if (pos is null)
            {
                pos = new DemoPaperPosition
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstrumentToken = token,
                    OpenContracts = 0,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                _demoPaperPositions.Add(pos);
            }

            pos.OpenContracts += lots;
            pos.UpdatedAtUtc = DateTimeOffset.UtcNow;
            openAfter = pos.OpenContracts;

            _demoPaperBuyLegs.Add(
                new DemoPaperBuyLeg
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstrumentToken = token,
                    ContractsRemaining = lots,
                    BoughtAtUtc = DateTimeOffset.UtcNow,
                });
        }
        else
        {
            if (pos is null || pos.OpenContracts < lots)
                throw new InvalidOperationException("Not enough open lots to sell.");

            await _demoPaperBuyLegs.ApplyFifoSellAsync(userId, token, lots, ct).ConfigureAwait(false);

            pos.OpenContracts -= lots;
            pos.UpdatedAtUtc = DateTimeOffset.UtcNow;
            openAfter = pos.OpenContracts;

            var nextBal = user.WalletBalance + legCash;
            if (nextBal > WalletService.MaxWalletBalance)
                throw new InvalidOperationException($"Wallet balance cannot exceed {WalletService.MaxWalletBalance:N2}.");

            user.WalletBalance = decimal.Round(nextBal, 2, MidpointRounding.AwayFromZero);
            cashFlow = legCash;
        }

        var executedAt = DateTimeOffset.UtcNow;
        _demoPaperTradeLogs.Add(
            new DemoPaperTradeLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InstrumentToken = token,
                Tradingsymbol = lockRow.Tradingsymbol,
                Exchange = lockRow.Exchange,
                Side = side,
                Contracts = lots,
                LastPrice = ltp,
                LotSize = lotMult,
                CashFlowInr = cashFlow,
                WalletBalanceAfter = user.WalletBalance,
                OpenContractsAfter = openAfter,
                ExecutedAtUtc = executedAt,
            });

        await _users.SaveChangesAsync(ct).ConfigureAwait(false);

        return new DemoPaperTradeResultDto(
            token,
            lockRow.Tradingsymbol,
            lockRow.Exchange,
            side,
            lots,
            ltp,
            lotMult,
            cashFlow,
            user.WalletBalance,
            openAfter);
    }

    public async Task SaveKiteInstrumentsChartZoomAsync(Guid userId, KiteInstrumentsChartZoomPutDto body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.InstrumentToken) || !body.InstrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token is required.");

        var token = body.InstrumentToken.Trim();
        if (body.VisibleFraction.HasValue && body.VisibleBars.HasValue)
        {
            throw new InvalidOperationException("Specify either visibleBars or visibleFraction, not both.");
        }

        var row = await _kiteChartSettings.GetAsync(userId, ct).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("User not found.");

        var dict = ParseChartZoomDict(row.ChartZoomByInstrumentTokenJson);
        if (body.VisibleFraction.HasValue)
        {
            var vf = body.VisibleFraction.Value;
            if (!double.IsFinite(vf))
                throw new InvalidOperationException("visibleFraction must be a finite number.");
            if (vf <= 0d)
                throw new InvalidOperationException("visibleFraction must be greater than zero.");
            if (vf >= 1d)
                dict.Remove(token);
            else
                dict[token] = Math.Round(vf, 6, MidpointRounding.AwayFromZero);
        }
        else if (body.VisibleBars is int vb)
        {
            if (vb < ChartZoomMinBars || vb > ChartZoomMaxBars)
            {
                throw new InvalidOperationException(
                    $"visibleBars must be between {ChartZoomMinBars} and {ChartZoomMaxBars}, or omit to clear.");
            }

            dict[token] = vb;
        }
        else
            dict.Remove(token);

        var json = dict.Count == 0 ? null : JsonSerializer.Serialize(dict, ChartZoomJsonOptions);
        await _kiteChartSettings.SaveChartZoomJsonAsync(userId, json, ct).ConfigureAwait(false);
    }

    public async Task SaveKiteInstrumentsChartIntervalOverrideAsync(
        Guid userId,
        KiteInstrumentsChartIntervalPutDto body,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.InstrumentToken) || !body.InstrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token is required.");

        var token = body.InstrumentToken.Trim();
        string? normalized = null;
        if (body.Interval is { } rawInterval)
        {
            if (string.IsNullOrWhiteSpace(rawInterval))
                throw new InvalidOperationException("interval must be non-empty when provided.");
            normalized = ChartUiIntervals.Normalize(rawInterval);
        }

        var row = await _kiteChartSettings.GetAsync(userId, ct).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("User not found.");

        var dict = ParseChartIntervalOverrideDict(row.ChartIntervalByInstrumentTokenJson);
        if (normalized is null)
            dict.Remove(token);
        else
            dict[token] = normalized;

        var json = dict.Count == 0 ? null : JsonSerializer.Serialize(dict, ChartZoomJsonOptions);
        await _kiteChartSettings.SaveChartIntervalByInstrumentTokenJsonAsync(userId, json, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<string>? ParseTrendAnalysisIntervalsFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json, ChartZoomJsonOptions);
            if (arr is null || arr.Count == 0)
                return null;

            return ChartUiIntervals.NormalizeTrendAnalysisSelection(arr);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string>? ParseChartIntervalOverrideMap(string? json)
    {
        var d = ParseChartIntervalOverrideDict(json);
        return d.Count == 0 ? null : d;
    }

    private static Dictionary<string, double>? ParseChartZoomMap(string? json)
    {
        var d = ParseChartZoomDict(json);
        return d.Count == 0 ? null : d;
    }

    private static Dictionary<string, string> ParseChartIntervalOverrideDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return d is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(d, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, double> ParseChartZoomDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, double>(StringComparer.Ordinal);

        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, double>>(json, ChartZoomJsonOptions);
            return d is null
                ? new Dictionary<string, double>(StringComparer.Ordinal)
                : new Dictionary<string, double>(d, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            try
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<string, int>>(json, ChartZoomJsonOptions);
                if (legacy is null || legacy.Count == 0)
                    return new Dictionary<string, double>(StringComparer.Ordinal);

                var converted = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var kv in legacy)
                    converted[kv.Key] = kv.Value;
                return converted;
            }
            catch (JsonException)
            {
                return new Dictionary<string, double>(StringComparer.Ordinal);
            }
        }
    }

    public async Task SaveKiteInstrumentsChartSettingsAsync(
        Guid userId,
        KiteInstrumentsChartSettingsDto settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Interval)
            || string.IsNullOrWhiteSpace(settings.RangePreset)
            || string.IsNullOrWhiteSpace(settings.GraphType))
            throw new InvalidOperationException("interval, rangePreset, and graphType are required.");

        var interval = NormalizeUiChartInterval(settings.Interval);
        var range = NormalizeChartRangePreset(settings.RangePreset);
        var graph = NormalizeChartGraphType(settings.GraphType);

        string? trendJson = null;
        if (settings.TrendAnalysisIntervals is not null)
        {
            var normalized = ChartUiIntervals.NormalizeTrendAnalysisSelection(settings.TrendAnalysisIntervals);
            trendJson = JsonSerializer.Serialize(normalized, ChartZoomJsonOptions);
        }

        await _kiteChartSettings
                .SaveAsync(
                    userId,
                    new KiteInstrumentsChartSettingsState(
                        interval,
                        range,
                        graph,
                        null,
                        null,
                        FavoriteMlAutomationEnabled: true,
                        TrendAnalysisIntervalsJson: trendJson),
                    ct)
                .ConfigureAwait(false);
    }

    public async Task SetFavoriteMlAutomationAsync(Guid userId, FavoriteMlAutomationPutDto body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        var row = await _kiteChartSettings.GetAsync(userId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("User not found.");

        string? intervalToStore = row.FavoriteMlAutomationInterval;
        if (body.Interval is not null)
        {
            if (string.IsNullOrWhiteSpace(body.Interval))
                intervalToStore = null;
            else
                intervalToStore = ChartUiIntervals.Normalize(body.Interval.Trim());
        }

        int? pollToStore = row.FavoriteMlAutomationPollIntervalSeconds;
        if (body.PollIntervalMinutes.HasValue)
        {
            var m = body.PollIntervalMinutes.Value;
            if (m == 0)
                pollToStore = null;
            else if (m < FavoriteMlThrottleMinMinutes || m > FavoriteMlThrottleMaxMinutes)
            {
                throw new InvalidOperationException(
                    $"pollIntervalMinutes must be between {FavoriteMlThrottleMinMinutes} and {FavoriteMlThrottleMaxMinutes} inclusive, or 0 to clear the per-user throttle.");
            }
            else
                pollToStore = m * 60;
        }

        int? minAfterOpenToStore = row.FavoriteMlAutomationMinSecondsAfterBarOpen;
        var minEl = body.MinSecondsAfterBarOpenForAutomation;
        if (minEl.ValueKind != JsonValueKind.Undefined)
        {
            if (minEl.ValueKind == JsonValueKind.Null)
                minAfterOpenToStore = null;
            else if (minEl.ValueKind == JsonValueKind.Number && minEl.TryGetInt32(out var sec))
            {
                if (sec < 0 || sec > FavoriteMlMinSecondsAfterBarOpenMax)
                {
                    throw new InvalidOperationException(
                        $"minSecondsAfterBarOpenForAutomation must be between 0 and {FavoriteMlMinSecondsAfterBarOpenMax} inclusive, or null to use the server default.");
                }

                minAfterOpenToStore = sec;
            }
            else
            {
                throw new InvalidOperationException(
                    "minSecondsAfterBarOpenForAutomation must be a JSON number or null.");
            }
        }

        await _kiteChartSettings
            .SaveFavoriteMlAutomationPreferencesAsync(
                userId,
                enabled: true,
                intervalToStore,
                pollToStore,
                minAfterOpenToStore,
                ct)
            .ConfigureAwait(false);
    }

    private async Task RequireUserExistsAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");
    }

    private static KiteInstrumentListItemDto MapTradingLockToDto(KiteTradingLockInstrument x) =>
        new(
            x.InstrumentToken,
            x.Tradingsymbol,
            x.Exchange,
            x.Name,
            x.InstrumentType,
            x.Segment,
            x.Expiry,
            x.Strike,
            x.LotSize);

    private static KiteInstrumentListItemDto MapFavoriteToDto(KiteFavoriteInstrument x) =>
        new(
            x.InstrumentToken,
            x.Tradingsymbol,
            x.Exchange,
            x.Name,
            x.InstrumentType,
            x.Segment,
            x.Expiry,
            x.Strike,
            x.LotSize);

    /// <summary>Whole minutes for API/SPA; legacy DB values use ceiling so e.g. 90s → 2.</summary>
    private static int? ThrottleSecondsToApiMinutes(int? seconds)
    {
        if (seconds is null or <= 0)
            return null;
        return (int)Math.Ceiling(seconds.Value / 60.0);
    }

    private static string? NullableNorm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{fieldName} is required.");
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? NormalizeNullablePrice(decimal? value)
    {
        if (!value.HasValue || value.Value <= 0m)
            return null;
        return value.Value;
    }

    private static string NormalizeValidity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "DAY";
        return value.Trim().ToUpperInvariant();
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

        if (normalized.Contains(BrokerZerodha, StringComparer.OrdinalIgnoreCase))
            return BrokerZerodha;
        if (normalized.Contains(BrokerGroww, StringComparer.OrdinalIgnoreCase))
            return BrokerGroww;
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

    private static void ValidateOrderTypePayload(string orderTypeRaw, decimal? price, decimal? triggerPrice)
    {
        var orderType = orderTypeRaw.Trim().ToUpperInvariant();
        if (orderType == "LIMIT" && price is null)
            throw new InvalidOperationException("LIMIT order requires price.");
        if (orderType == "SL" && (price is null || triggerPrice is null))
            throw new InvalidOperationException("SL order requires both price and triggerPrice.");
        if (orderType == "SL-M" && triggerPrice is null)
            throw new InvalidOperationException("SL-M order requires triggerPrice.");
    }

    private static string NormalizeScalperIntervalOrDefault(string? intervalRaw)
    {
        var x = string.IsNullOrWhiteSpace(intervalRaw) ? "" : intervalRaw.Trim().ToLowerInvariant();
        return x switch
        {
            "1m" or "3m" or "5m" => x,
            _ => "1m",
        };
    }

    private static string NormalizeScalperRangePresetOrDefault(string? presetRaw)
    {
        var x = string.IsNullOrWhiteSpace(presetRaw) ? "" : presetRaw.Trim().ToLowerInvariant();
        return x switch
        {
            "last15m" or "last30m" or "last1h" or "last5h" or "last1d" or "last3d" => x,
            _ => "last3d",
        };
    }

    private static string NormalizeScalperGraphTypeOrDefault(string? graphTypeRaw)
    {
        var x = string.IsNullOrWhiteSpace(graphTypeRaw) ? "" : graphTypeRaw.Trim().ToLowerInvariant();
        return x switch
        {
            "candlestick" or "line" or "bar" => x,
            _ => "candlestick",
        };
    }

    private static decimal NormalizeScalperPointsOrDefault(decimal? raw, decimal fallback)
    {
        var v = raw ?? fallback;
        if (v <= 0m)
            return fallback;
        if (v > 1_000_000m)
            return 1_000_000m;
        return decimal.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeUiChartInterval(string interval) => ChartUiIntervals.Normalize(interval);

    private static string NormalizeChartRangePreset(string preset)
    {
        var t = preset.Trim().ToLowerInvariant();
        return t switch
        {
            "auto" or "last5m" or "last10m" or "last15m" or "last30m" or "last1h" or "last5h" or "last10h"
                or "last1d" or "last2d" or "last5d" or "last1mo" => t,
            _ => throw new InvalidOperationException(
                "rangePreset must be one of: auto, last5m, last10m, last15m, last30m, last1h, last5h, last10h, last1d, last2d, last5d, last1mo."),
        };
    }

    private static string NormalizeChartGraphType(string graphType)
    {
        var t = graphType.Trim().ToLowerInvariant();
        return t switch
        {
            "line" or "bar" or "candlestick" or "trend" => t,
            _ => throw new InvalidOperationException("graphType must be line, bar, candlestick, or trend."),
        };
    }

    private static DateTimeOffset ComputeMaWarmupFetchStart(DateTimeOffset requestStart, string code)
    {
        var bar = ChartBarDuration(code);
        var delta = TimeSpan.FromTicks(bar.Ticks * ChartMovingAverages.WarmupBarCount);
        return requestStart - delta;
    }

    private static KiteHistoricalCandlesDto FinalizeChartHistoricalCandles(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        string intervalCode,
        DateTimeOffset requestStart,
        DateTimeOffset requestEnd)
    {
        var ordered = candles.OrderBy(c => c.Time).ToList();
        var withMa = ChartMovingAverages.Attach(ordered);
        var trimmed = withMa.Where(c => c.Time >= requestStart).ToList();
        return new KiteHistoricalCandlesDto(trimmed, intervalCode, requestStart, requestEnd);
    }

    /// <summary>One chart bar length for the UI interval (used to extend Kite fetch for MA warmup).</summary>
    private static TimeSpan ChartBarDuration(string code) => ChartUiIntervals.BarDuration(code);

    private static TimeSpan DefaultChartLookback(string code) =>
        code switch
        {
            "1m" or "2m" or "4m" => TimeSpan.FromDays(5),
            "3m" => TimeSpan.FromDays(30),
            "5m" => TimeSpan.FromDays(60),
            "10m" => TimeSpan.FromDays(90),
            "15m" => TimeSpan.FromDays(120),
            "30m" => TimeSpan.FromDays(180),
            "1h" => TimeSpan.FromDays(365),
            "90m" => TimeSpan.FromDays(365),
            "2h" => TimeSpan.FromDays(540),
            "4h" => TimeSpan.FromDays(730),
            "8h" => TimeSpan.FromDays(1095),
            "1d" => TimeSpan.FromDays(730),
            "1w" => TimeSpan.FromDays(365 * 12),
            _ => TimeSpan.FromDays(5),
        };

    private static string MapUiIntervalToKite(string code) =>
        code switch
        {
            "1m" => "minute",
            "3m" => "3minute",
            "5m" => "5minute",
            "10m" => "10minute",
            "15m" => "15minute",
            "30m" => "30minute",
            "1h" => "60minute",
            "1d" => "day",
            _ => throw new InvalidOperationException("Unsupported interval."),
        };

    private static IReadOnlyList<KiteHistoricalCandlePointDto> MergeMinuteCandles(
        IReadOnlyList<KiteHistoricalCandlePointDto> minutes,
        int periodMinutes)
    {
        if (minutes.Count == 0 || periodMinutes <= 1)
            return minutes;

        var ordered = minutes.OrderBy(c => c.Time).ToList();
        return MergeOhlcByBucketSeconds(ordered, periodMinutes * 60L);
    }

    private static IReadOnlyList<KiteHistoricalCandlePointDto> MergeOhlcByBucketSeconds(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        long bucketSeconds)
    {
        if (candles.Count == 0 || bucketSeconds < 1)
            return candles;

        var ordered = candles.OrderBy(c => c.Time).ToList();
        var merged = new List<KiteHistoricalCandlePointDto>();
        long? bucketKey = null;
        decimal open = 0, high = 0, low = 0, close = 0;
        long volume = 0;
        var haveOpen = false;

        void Flush()
        {
            if (!haveOpen || bucketKey is null)
                return;

            merged.Add(new KiteHistoricalCandlePointDto(
                DateTimeOffset.FromUnixTimeSeconds(bucketKey.Value),
                open,
                high,
                low,
                close,
                volume));
        }

        foreach (var c in ordered)
        {
            var secs = c.Time.ToUnixTimeSeconds();
            var key = secs - secs % bucketSeconds;
            if (bucketKey != key)
            {
                Flush();
                bucketKey = key;
                open = c.Open;
                high = c.High;
                low = c.Low;
                close = c.Close;
                volume = c.Volume;
                haveOpen = true;
            }
            else
            {
                high = Math.Max(high, c.High);
                low = Math.Min(low, c.Low);
                close = c.Close;
                volume += c.Volume;
            }
        }

        Flush();
        return merged;
    }

    /// <summary>Group consecutive daily bars into blocks of <paramref name="n"/> (7 → ~weekly bars for UI <c>1w</c>).</summary>
    private static IReadOnlyList<KiteHistoricalCandlePointDto> MergeEveryNCandles(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        int n)
    {
        if (n <= 1 || candles.Count == 0)
            return candles;

        var ordered = candles.OrderBy(c => c.Time).ToList();
        var merged = new List<KiteHistoricalCandlePointDto>();
        for (var i = 0; i < ordered.Count; i += n)
        {
            var chunk = ordered.Skip(i).Take(n).ToList();
            if (chunk.Count == 0)
                break;

            var o = chunk[0].Open;
            var hi = chunk.Max(x => x.High);
            var lo = chunk.Min(x => x.Low);
            var last = chunk[^1];
            var vol = chunk.Sum(x => x.Volume);
            merged.Add(new KiteHistoricalCandlePointDto(last.Time, o, hi, lo, last.Close, vol));
        }

        return merged;
    }

    private async Task<(string ApiKey, string AccessToken)> RequireKiteInstrumentSessionAsync(
        Guid userId,
        CancellationToken ct)
    {
        var userExists = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (userExists is null)
            throw new InvalidOperationException("User not found.");

        var accessToken = await _brokerSetup.GetKiteAccessTokenAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("No valid Kite session. Reconnect Zerodha.");

        var apiKey = _kiteOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variable ZerodhaKite__ApiKey (see README).");

        return (apiKey, accessToken);
    }

    /// <summary>Maps callback <paramref name="state"/> to the HMAC payload. Returns the server cache key when resolved from memory (for one-time removal).</summary>
    private (string SignedState, string? PendingKey) ResolveKiteOAuthState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return (state, null);

        var trimmed = state.Trim();

        if (trimmed.Length == 32
            && trimmed.All(char.IsAsciiHexDigit)
            && _memoryCache.TryGetValue(
                PendingStateCachePrefix + trimmed.ToLowerInvariant(),
                out var cached)
            && cached is string full
            && !string.IsNullOrEmpty(full))
        {
            return (full, trimmed.ToLowerInvariant());
        }

        return (trimmed, null);
    }

    /// <summary>Kite CSV expiry is typically <c>yyyy-MM-dd</c>; derivatives without a parseable date sort after dated rows.</summary>
    private static int CompareInstrumentExpiryAscending(KiteInstrumentListItemDto a, KiteInstrumentListItemDto b)
    {
        var da = TryParseKiteExpiryDate(a.Expiry);
        var db = TryParseKiteExpiryDate(b.Expiry);
        if (da.HasValue && db.HasValue)
            return da.Value.CompareTo(db.Value);
        if (da.HasValue)
            return -1;
        if (db.HasValue)
            return 1;
        return string.CompareOrdinal(a.Tradingsymbol ?? "", b.Tradingsymbol ?? "");
    }

    private static DateOnly? TryParseKiteExpiryDate(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
            return null;
        return DateOnly.TryParse(expiry.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
