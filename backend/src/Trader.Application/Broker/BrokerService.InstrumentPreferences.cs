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

public sealed partial class BrokerService
{
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
            triggerPts,
            row.GttEnabled);
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
                triggerPts,
                body.GttEnabled),
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
}
