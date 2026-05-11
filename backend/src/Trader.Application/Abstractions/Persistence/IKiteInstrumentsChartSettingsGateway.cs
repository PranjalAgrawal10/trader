namespace Trader.Application.Abstractions.Persistence;

/// <param name="FavoriteMlAutomationEnabled">Null when saving toolbar-only so the DB flag is left unchanged.</param>
public sealed record KiteInstrumentsChartSettingsState(
    string? Interval,
    string? RangePreset,
    string? GraphType,
    string? ChartZoomByInstrumentTokenJson,
    string? ChartIntervalByInstrumentTokenJson,
    bool? FavoriteMlAutomationEnabled,
    string? FavoriteMlAutomationInterval = null,
    int? FavoriteMlAutomationPollIntervalSeconds = null,
    /// <summary>JSON array string from DB on read; on save, non-null replaces <c>KiteInstrumentsTrendAnalysisIntervalsJson</c>.</summary>
    string? TrendAnalysisIntervalsJson = null,
    int? FavoriteMlAutomationMinSecondsAfterBarOpen = null,
    bool? DemoAutoTradeEnabled = null,
    string? DemoAutoTradeStrategy = null);

public interface IKiteInstrumentsChartSettingsGateway
{
    Task<KiteInstrumentsChartSettingsState?> GetAsync(Guid userId, CancellationToken ct = default);

    Task SaveAsync(Guid userId, KiteInstrumentsChartSettingsState settings, CancellationToken ct = default);

    Task SetFavoriteMlAutomationAsync(Guid userId, bool enabled, CancellationToken ct = default);

    /// <summary>Updates favorite-ML automation toggle and optional per-user candle interval / new-pass throttle / intrabar delay.</summary>
    Task SaveFavoriteMlAutomationPreferencesAsync(
        Guid userId,
        bool enabled,
        string? favoriteMlAutomationInterval,
        int? favoriteMlAutomationPollIntervalSeconds,
        int? favoriteMlAutomationMinSecondsAfterBarOpen,
        CancellationToken ct = default);

    /// <summary>Sets the user's last automated new-prediction pass instant (for per-user poll throttling).</summary>
    Task SetFavoriteMlAutomationLastNewPassUtcAsync(Guid userId, DateTimeOffset utc, CancellationToken ct = default);

    /// <summary>Persists only the instruments-page chart zoom map JSON (other chart fields unchanged).</summary>
    Task SaveChartZoomJsonAsync(Guid userId, string? chartZoomByInstrumentTokenJson, CancellationToken ct = default);

    /// <summary>Persists only the per-instrument chart interval override map JSON (other chart fields unchanged).</summary>
    Task SaveChartIntervalByInstrumentTokenJsonAsync(Guid userId, string? chartIntervalByInstrumentTokenJson, CancellationToken ct = default);

    /// <summary>Persists instruments-page demo auto-trade toggle and optional strategy (canonical id when non-null).</summary>
    Task SetDemoAutoTradePreferencesAsync(Guid userId, bool enabled, string? normalizedStrategyOrNull, CancellationToken ct = default);
}
