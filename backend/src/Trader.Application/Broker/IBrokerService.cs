namespace Trader.Application.Broker;

public interface IBrokerService
{
    Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Marks broker setup complete without a live broker (demo / placeholder).</summary>
    Task CompleteSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Kite login URL including <c>state</c>. Uses a short server-stored key so Zerodha does not truncate a long signed payload.
    /// </summary>
    Task<KiteLoginUrlBuildResult> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Completes OAuth using request_token and state from Kite redirect.</summary>
    Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default);

    /// <summary>Clears stored broker session and onboarding completion for the user.</summary>
    Task<BrokerStatusDto> DisconnectAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Full F&O master (NFO + BFO) and MCX commodity instruments from Kite’s daily CSV dumps.
    /// </summary>
    Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Substring search across the Kite instrument CSVs for the given segment (streams until enough matches).</summary>
    Task<KiteInstrumentSearchDto> SearchKiteInstrumentsAsync(
        Guid userId,
        string query,
        KiteInstrumentSearchSegment segment,
        CancellationToken ct = default);

    /// <summary>
    /// Historical OHLCV from Kite for an instrument token. <paramref name="interval"/> is a UI code (<c>1m</c>, <c>2m</c>, … <c>1d</c>).
    /// Optional <paramref name="fromUtc"/> / <paramref name="toUtc"/> bound the range; when omitted, a default lookback is used.
    /// </summary>
    Task<KiteHistoricalCandlesDto> GetKiteHistoricalCandlesAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default);

    Task<KiteFavoriteInstrumentsListDto> GetKiteFavoriteInstrumentsAsync(Guid userId, CancellationToken ct = default);

    Task AddKiteFavoriteInstrumentAsync(Guid userId, KiteInstrumentListItemDto item, CancellationToken ct = default);

    Task RemoveKiteFavoriteInstrumentAsync(Guid userId, string instrumentToken, CancellationToken ct = default);

    /// <summary>Loads saved chart UI settings for the Kite instruments page (null fields mean “use SPA defaults”).</summary>
    Task<KiteInstrumentsChartSettingsDto> GetKiteInstrumentsChartSettingsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Persists chart UI settings after validation.</summary>
    Task SaveKiteInstrumentsChartSettingsAsync(Guid userId, KiteInstrumentsChartSettingsDto settings, CancellationToken ct = default);

    /// <summary>Updates whether background favorite-ML automation is allowed for this user (server still requires global option + Kite).</summary>
    Task SetFavoriteMlAutomationEnabledAsync(Guid userId, bool enabled, CancellationToken ct = default);

    /// <summary>Updates or clears saved zoom (visible bar count) for one instrument token.</summary>
    Task SaveKiteInstrumentsChartZoomAsync(Guid userId, KiteInstrumentsChartZoomPutDto body, CancellationToken ct = default);

    /// <summary>Sets or clears a per-instrument candle interval override (null interval = use page default for that token).</summary>
    Task SaveKiteInstrumentsChartIntervalOverrideAsync(
        Guid userId,
        KiteInstrumentsChartIntervalPutDto body,
        CancellationToken ct = default);
}
