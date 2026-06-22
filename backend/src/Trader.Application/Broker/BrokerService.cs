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

public sealed partial class BrokerService : IBrokerService
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
}
