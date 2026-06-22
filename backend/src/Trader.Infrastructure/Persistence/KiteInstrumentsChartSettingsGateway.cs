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
                u.KiteInstrumentsChartIntervalByInstrumentTokenJson,
                u.FavoriteMlAutomationEnabled,
                u.FavoriteMlAutomationInterval,
                u.FavoriteMlAutomationPollIntervalSeconds,
                u.KiteInstrumentsTrendAnalysisIntervalsJson,
                u.FavoriteMlAutomationMinSecondsAfterBarOpen,
                u.DemoAutoTradeEnabled,
                u.DemoAutoTradeStrategy))
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
        if (settings.TrendAnalysisIntervalsJson is not null)
            user.KiteInstrumentsTrendAnalysisIntervalsJson = settings.TrendAnalysisIntervalsJson;
        if (settings.DemoAutoTradeEnabled is bool demo)
            user.DemoAutoTradeEnabled = demo;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetFavoriteMlAutomationAsync(Guid userId, bool enabled, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.FavoriteMlAutomationEnabled = enabled;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveFavoriteMlAutomationPreferencesAsync(
        Guid userId,
        bool enabled,
        string? favoriteMlAutomationInterval,
        int? favoriteMlAutomationPollIntervalSeconds,
        int? favoriteMlAutomationMinSecondsAfterBarOpen,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.FavoriteMlAutomationEnabled = enabled;
        user.FavoriteMlAutomationInterval = favoriteMlAutomationInterval;
        user.FavoriteMlAutomationPollIntervalSeconds = favoriteMlAutomationPollIntervalSeconds;
        user.FavoriteMlAutomationMinSecondsAfterBarOpen = favoriteMlAutomationMinSecondsAfterBarOpen;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetFavoriteMlAutomationLastNewPassUtcAsync(Guid userId, DateTimeOffset utc, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.FavoriteMlAutomationLastNewPassUtc = utc;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveChartZoomJsonAsync(Guid userId, string? chartZoomByInstrumentTokenJson, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.KiteInstrumentsChartZoomJson = chartZoomByInstrumentTokenJson;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveChartIntervalByInstrumentTokenJsonAsync(
        Guid userId,
        string? chartIntervalByInstrumentTokenJson,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.KiteInstrumentsChartIntervalByInstrumentTokenJson = chartIntervalByInstrumentTokenJson;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetDemoAutoTradePreferencesAsync(
        Guid userId,
        bool enabled,
        string? normalizedStrategyOrNull,
        CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.DemoAutoTradeEnabled = enabled;
        if (!string.IsNullOrWhiteSpace(normalizedStrategyOrNull))
            user.DemoAutoTradeStrategy = normalizedStrategyOrNull.Trim();
        await _db.SaveChangesAsync(ct);
    }

    public Task<ScalperSettingsState?> GetScalperSettingsAsync(Guid userId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new ScalperSettingsState(
                u.ScalperInterval,
                u.ScalperRangePreset,
                u.ScalperGraphType,
                u.ScalperShowVolume,
                u.ScalperSafeModeEnabled,
                u.ScalperSafeStopLossPoints,
                u.ScalperSafeTriggerPoints,
                u.ScalperGttEnabled))
            .FirstOrDefaultAsync(ct);

    public async Task SaveScalperSettingsAsync(Guid userId, ScalperSettingsState settings, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException("User not found.");
        user.ScalperInterval = settings.Interval;
        user.ScalperRangePreset = settings.RangePreset;
        user.ScalperGraphType = settings.GraphType;
        user.ScalperShowVolume = settings.ShowVolume;
        user.ScalperSafeModeEnabled = settings.SafeModeEnabled;
        user.ScalperSafeStopLossPoints = settings.SafeStopLossPoints;
        user.ScalperSafeTriggerPoints = settings.SafeTriggerPoints;
        user.ScalperGttEnabled = settings.GttEnabled;
        await _db.SaveChangesAsync(ct);
    }
}
