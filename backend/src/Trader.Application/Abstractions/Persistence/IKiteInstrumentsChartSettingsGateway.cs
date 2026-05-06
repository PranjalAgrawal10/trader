namespace Trader.Application.Abstractions.Persistence;

public sealed record KiteInstrumentsChartSettingsState(
    string? Interval,
    string? RangePreset,
    string? GraphType);

public interface IKiteInstrumentsChartSettingsGateway
{
    Task<KiteInstrumentsChartSettingsState?> GetAsync(Guid userId, CancellationToken ct = default);

    Task SaveAsync(Guid userId, KiteInstrumentsChartSettingsState settings, CancellationToken ct = default);
}
