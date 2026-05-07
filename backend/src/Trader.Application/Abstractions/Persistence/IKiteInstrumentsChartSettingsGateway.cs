namespace Trader.Application.Abstractions.Persistence;

public sealed record KiteInstrumentsChartSettingsState(
    string? Interval,
    string? RangePreset,
    string? GraphType,
    string? ChartZoomByInstrumentTokenJson);

public interface IKiteInstrumentsChartSettingsGateway
{
    Task<KiteInstrumentsChartSettingsState?> GetAsync(Guid userId, CancellationToken ct = default);

    Task SaveAsync(Guid userId, KiteInstrumentsChartSettingsState settings, CancellationToken ct = default);

    /// <summary>Persists only the instruments-page chart zoom map JSON (other chart fields unchanged).</summary>
    Task SaveChartZoomJsonAsync(Guid userId, string? chartZoomByInstrumentTokenJson, CancellationToken ct = default);
}
