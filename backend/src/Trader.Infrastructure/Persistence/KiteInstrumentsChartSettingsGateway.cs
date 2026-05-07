using Microsoft.EntityFrameworkCore;
using Trader.Application.Abstractions.Persistence;

namespace Trader.Infrastructure.Persistence;

public sealed class KiteInstrumentsChartSettingsGateway : IKiteInstrumentsChartSettingsGateway
{
    private readonly TraderDbContext _db;

    public KiteInstrumentsChartSettingsGateway(TraderDbContext db)
    {
        _db = db;
    }

    public Task<KiteInstrumentsChartSettingsState?> GetAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new KiteInstrumentsChartSettingsState(
                u.KiteInstrumentsChartInterval,
                u.KiteInstrumentsChartRangePreset,
                u.KiteInstrumentsChartGraphType,
                u.KiteInstrumentsChartZoomJson,
                u.FavoriteMlAutomationEnabled))
            .FirstOrDefaultAsync(ct);

    public async Task SaveAsync(Guid userId, KiteInstrumentsChartSettingsState settings, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");

        user.KiteInstrumentsChartInterval = settings.Interval;
        user.KiteInstrumentsChartRangePreset = settings.RangePreset;
        user.KiteInstrumentsChartGraphType = settings.GraphType;
        if (settings.FavoriteMlAutomationEnabled is bool ml)
            user.FavoriteMlAutomationEnabled = ml;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetFavoriteMlAutomationAsync(Guid userId, bool enabled, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.FavoriteMlAutomationEnabled = enabled;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveChartZoomJsonAsync(Guid userId, string? chartZoomByInstrumentTokenJson, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.KiteInstrumentsChartZoomJson = chartZoomByInstrumentTokenJson;
        await _db.SaveChangesAsync(ct);
    }
}
