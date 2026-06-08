namespace Trader.Application.Broker;

public interface IBrokerService
{
    Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<BrokerProviderAvailabilityDto>> GetOrderBrokerProvidersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Marks broker setup complete without a live broker (demo / placeholder).</summary>
    Task CompleteSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Kite login URL including <c>state</c>. Uses a short server-stored key so Zerodha does not truncate a long signed payload.
    /// </summary>
    Task<KiteLoginUrlBuildResult> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Completes OAuth using request_token and state from Kite redirect.</summary>
    Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default);

    Task<BrokerStatusDto> ConnectGrowwAsync(Guid userId, GrowwConnectRequestDto body, CancellationToken ct = default);

    Task<BrokerStatusDto> SetActiveBrokerAsync(Guid userId, string broker, CancellationToken ct = default);

    /// <summary>Clears stored broker session and onboarding completion for the user.</summary>
    Task<BrokerStatusDto> DisconnectAsync(Guid userId, string? broker = null, CancellationToken ct = default);

    /// <summary>
    /// Full F&O master (NFO + BFO) and MCX commodity instruments from Kite’s daily CSV dumps.
    /// </summary>
    Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Ranks capped F&amp;O+MCX preview contracts by intraday-style % vs Kite OHLC prior close (<c>/quote/ohlc</c>).</summary>
    Task<KiteTodayTopPerformersDto> GetKiteTodayTopPerformersAsync(Guid userId, int take, CancellationToken ct = default);

    /// <summary>Substring search across the Kite instrument CSVs for the given segment (streams until enough matches).</summary>
    Task<KiteInstrumentSearchDto> SearchKiteInstrumentsAsync(
        Guid userId,
        string query,
        KiteInstrumentSearchSegment segment,
        CancellationToken ct = default);

    /// <remarks>
    /// Combined SMA/EMA/SR payloads (use <see cref="GetKiteHistoricalChartOhlcvAsync"/> +
    /// <see cref="GetKiteHistoricalChartOverlaysAsync"/> for lighter parallel responses; server caches the full series ~25s per key).
    /// </remarks>
    Task<KiteHistoricalCandlesDto> GetKiteHistoricalCandlesAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default);

    /// <summary>OHLCV only (no moving averages/SR columns) — same range semantics as historical-candles.</summary>
    Task<KiteHistoricalOhlcvOnlyDto> GetKiteHistoricalChartOhlcvAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default);

    /// <summary>Overlay columns aligned to the same trimmed window as OHLC-only (paired fetch).</summary>
    Task<KiteHistoricalOverlaysDto> GetKiteHistoricalChartOverlaysAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default);

    /// <summary>LTP plus prior-session close quote (via Kite <c>/quote/ohlc</c>, ~5s server cache).</summary>
    Task<KiteInstrumentLiveQuoteDto> GetKiteInstrumentLiveQuoteAsync(
        Guid userId,
        string exchange,
        string tradingsymbol,
        CancellationToken ct = default);

    /// <summary>Kite orderbook for the day (<c>GET /orders</c>), including all interim/final statuses.</summary>
    Task<KiteOrderBookDto> GetKiteOrdersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Current Kite net positions (from <c>GET /portfolio/positions</c> net array).</summary>
    Task<IReadOnlyList<KiteNetPositionDto>> GetKiteNetPositionsAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<KiteNetPositionDto>> GetNetPositionsAsync(
        Guid userId,
        string? broker,
        CancellationToken ct = default);

    Task<KiteOrderActionResultDto> CancelKiteOrderAsync(
        Guid userId,
        string orderId,
        KiteOrderCancelRequestDto body,
        CancellationToken ct = default);

    Task<KiteOrderActionResultDto> ModifyKiteOrderAsync(
        Guid userId,
        string orderId,
        KiteOrderModifyRequestDto body,
        CancellationToken ct = default);

    Task<KiteOrderActionResultDto> RepeatKiteOrderAsync(
        Guid userId,
        string sourceOrderId,
        KiteOrderRepeatRequestDto body,
        CancellationToken ct = default);

    Task<KiteOrderActionResultDto> PlaceKiteOrderAsync(
        Guid userId,
        KiteOrderPlaceRequestDto body,
        CancellationToken ct = default);

    Task<KiteOrderActionResultDto> PlaceOrderAsync(
        Guid userId,
        KiteOrderPlaceRequestDto body,
        CancellationToken ct = default);

    Task<ScalperSettingsDto> GetScalperSettingsAsync(Guid userId, CancellationToken ct = default);

    Task SaveScalperSettingsAsync(Guid userId, ScalperSettingsPutDto body, CancellationToken ct = default);

    Task<KiteFavoriteInstrumentsListDto> GetKiteFavoriteInstrumentsAsync(Guid userId, CancellationToken ct = default);

    Task AddKiteFavoriteInstrumentAsync(Guid userId, KiteInstrumentListItemDto item, CancellationToken ct = default);

    Task RemoveKiteFavoriteInstrumentAsync(Guid userId, string instrumentToken, CancellationToken ct = default);

    Task<KiteTradingLocksListDto> GetKiteTradingLocksAsync(Guid userId, CancellationToken ct = default);

    Task AddKiteTradingLockAsync(Guid userId, KiteInstrumentListItemDto item, CancellationToken ct = default);

    Task RemoveKiteTradingLockAsync(Guid userId, string instrumentToken, CancellationToken ct = default);

    /// <summary>Loads saved chart UI settings for the Kite instruments page (null fields mean “use SPA defaults”).</summary>
    Task<KiteInstrumentsChartSettingsDto> GetKiteInstrumentsChartSettingsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Persists chart UI settings after validation.</summary>
    Task SaveKiteInstrumentsChartSettingsAsync(Guid userId, KiteInstrumentsChartSettingsDto settings, CancellationToken ct = default);

    /// <summary>Updates favorite-ML automation toggle and optional per-user candle interval / new-pass throttle.</summary>
    Task SetFavoriteMlAutomationAsync(Guid userId, FavoriteMlAutomationPutDto body, CancellationToken ct = default);

    /// <summary>Persists demo auto-trade toggle and optional strategy preset (no live orders).</summary>
    Task SetDemoAutoTradePreferencesAsync(Guid userId, bool enabled, string? strategyRaw, CancellationToken ct = default);

    /// <summary>Open demo paper longs (whole contracts). Enriched from trading locks where possible.</summary>
    Task<IReadOnlyList<DemoPaperPositionListItemDto>> GetDemoPaperPositionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Recent manual demo paper fills for the Manual trade tab (newest first).</summary>
    Task<IReadOnlyList<DemoPaperTradeHistoryRowDto>> GetDemoPaperTradeHistoryAsync(
        Guid userId,
        int? take = null,
        CancellationToken ct = default);

    /// <summary>Market-style paper trade at cached LTP: buy debits wallet and adds contracts; sell credits wallet and reduces open long.</summary>
    Task<DemoPaperTradeResultDto> ExecuteDemoPaperTradeAsync(
        Guid userId,
        DemoPaperTradeRequestDto request,
        CancellationToken ct = default);

    /// <summary>Updates or clears saved zoom (visible bar count) for one instrument token.</summary>
    Task SaveKiteInstrumentsChartZoomAsync(Guid userId, KiteInstrumentsChartZoomPutDto body, CancellationToken ct = default);

    /// <summary>Sets or clears a per-instrument candle interval override (null interval = use page default for that token).</summary>
    Task SaveKiteInstrumentsChartIntervalOverrideAsync(
        Guid userId,
        KiteInstrumentsChartIntervalPutDto body,
        CancellationToken ct = default);
}
