namespace Trader.Application.Abstractions.Persistence;

/// <param name="FavoriteMlAutomationEnabled">Null when saving toolbar-only so the DB flag is left unchanged.</param>
public sealed record KiteInstrumentsChartSettingsState(
    string? Interval,
    string? RangePreset,
    string? GraphType,
    string? ChartZoomByInstrumentTokenJson,
    bool? FavoriteMlAutomationEnabled);

public interface IKiteInstrumentsChartSettingsGateway
{
    Task<KiteInstrumentsChartSettingsState?> GetAsync(Guid userId, CancellationToken ct = default);

    Task SaveAsync(Guid userId, KiteInstrumentsChartSettingsState settings, CancellationToken ct = default);

    Task SetFavoriteMlAutomationAsync(Guid userId, bool enabled, CancellationToken ct = default);

    /// <summary>Persists only the instruments-page chart zoom map JSON (other chart fields unchanged).</summary>
    Task SaveChartZoomJsonAsync(Guid userId, string? chartZoomByInstrumentTokenJson, CancellationToken ct = default);
}
