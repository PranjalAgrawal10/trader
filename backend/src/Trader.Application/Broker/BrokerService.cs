using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Configuration;
using Trader.Domain.Entities;

namespace Trader.Application.Broker;

public sealed class BrokerService : IBrokerService
{
    private const int ChartZoomMinBars = 1;
    private const int ChartZoomMaxBars = 500_000;
    private static readonly JsonSerializerOptions ChartZoomJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string PendingStateCachePrefix = "Trader.KiteOAuth.PendingState:";
    private static readonly TimeSpan PendingStateTtl = TimeSpan.FromMinutes(20);

    /// <summary>Row cap per Kite <c>/instruments/{exchange}</c> response (streaming stops early; truncated flag set when more rows exist).</summary>
    private const int KiteInstrumentsMaxRowsPerExchange = 100;

    private const int KiteInstrumentSearchMaxMatchesFnoPerExchange = 50;
    private const int KiteInstrumentSearchMaxMatchesMcx = 100;
    private const int KiteInstrumentSearchQueryMaxLength = 128;

    private const int MaxKiteFavoriteInstrumentsPerUser = 400;

    private readonly IBrokerSetupGateway _brokerSetup;
    private readonly IKiteOAuthStateCodec _stateCodec;
    private readonly IKiteSessionExchange _kiteSessionExchange;
    private readonly IKiteInstrumentsClient _kiteInstruments;
    private readonly IKiteFavoriteInstrumentRepository _kiteFavoriteInstruments;
    private readonly IKiteInstrumentsChartSettingsGateway _kiteChartSettings;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly IMemoryCache _memoryCache;

    public BrokerService(
        IBrokerSetupGateway brokerSetup,
        IKiteOAuthStateCodec stateCodec,
        IKiteSessionExchange kiteSessionExchange,
        IKiteInstrumentsClient kiteInstruments,
        IKiteFavoriteInstrumentRepository kiteFavoriteInstruments,
        IKiteInstrumentsChartSettingsGateway kiteChartSettings,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        IMemoryCache memoryCache)
    {
        _brokerSetup = brokerSetup;
        _stateCodec = stateCodec;
        _kiteSessionExchange = kiteSessionExchange;
        _kiteInstruments = kiteInstruments;
        _kiteFavoriteInstruments = kiteFavoriteInstruments;
        _kiteChartSettings = kiteChartSettings;
        _kiteOptions = kiteOptions;
        _memoryCache = memoryCache;
    }

    public async Task<BrokerStatusDto> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");

        var provider = snapshot.BrokerProvider;
        var at = snapshot.BrokerConnectedAt;
        return new BrokerStatusDto(!string.IsNullOrEmpty(provider), at, provider);
    }

    public Task CompleteSetupAsync(Guid userId, CancellationToken ct = default) =>
        _brokerSetup.CompleteBrokerSetupAsync(userId, ct);

    public Task<KiteLoginUrlBuildResult> GetKiteLoginUrlAsync(Guid userId, CancellationToken ct = default)
    {
        _ = ct;
        var opt = _kiteOptions.Value;
        if (string.IsNullOrWhiteSpace(opt.ApiKey) || string.IsNullOrWhiteSpace(opt.RedirectUrl))
        {
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variables ZerodhaKite__ApiKey and ZerodhaKite__RedirectUrl (or use .env.development in Development; see README).");
        }

        var fullState = _stateCodec.Encode(userId);
        var shortKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _memoryCache.Set(PendingStateCachePrefix + shortKey, fullState, PendingStateTtl);

        // redirect_params: Kite appends these to the redirect URL (see kite.trade docs). Backup if `state` is omitted.
        var redirectParams = Uri.EscapeDataString($"trader_oauth={shortKey}");
        var url =
            $"https://kite.zerodha.com/connect/login?v=3&api_key={Uri.EscapeDataString(opt.ApiKey)}&state={Uri.EscapeDataString(shortKey)}&redirect_params={redirectParams}";
        return Task.FromResult(new KiteLoginUrlBuildResult(url, shortKey));
    }

    public async Task<BrokerStatusDto> CompleteKiteOAuthAsync(string requestToken, string state, CancellationToken ct = default)
    {
        var (signedState, pendingKey) = ResolveKiteOAuthState(state);
        var userId = _stateCodec.TryDecode(signedState)
                     ?? throw new InvalidOperationException("Invalid or expired OAuth state. Try connecting again.");

        var exchanged = await _kiteSessionExchange.ExchangeAsync(requestToken, ct);
        if (!exchanged.Success || string.IsNullOrEmpty(exchanged.AccessToken) || string.IsNullOrEmpty(exchanged.KiteUserId))
        {
            throw new InvalidOperationException(exchanged.ErrorMessage ?? "Could not complete Kite login.");
        }

        await _brokerSetup.PersistKiteSessionAsync(
            userId,
            new BrokerKitePersistRequest(exchanged.AccessToken, exchanged.RefreshToken, exchanged.KiteUserId),
            ct);

        if (pendingKey is not null)
            _memoryCache.Remove(PendingStateCachePrefix + pendingKey);

        return await GetStatusAsync(userId, ct);
    }

    public async Task<BrokerStatusDto> DisconnectAsync(Guid userId, CancellationToken ct = default)
    {
        await _brokerSetup.DisconnectBrokerAsync(userId, ct);
        return await GetStatusAsync(userId, ct);
    }

    public async Task<KiteFnoCommodityListsDto> GetKiteFnoCommodityInstrumentsAsync(Guid userId, CancellationToken ct = default)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        var cap = (int?)KiteInstrumentsMaxRowsPerExchange;
        var nfoTask = _kiteInstruments.FetchExchangeInstrumentsAsync("NFO", apiKey, accessToken, cap, ct);
        var bfoTask = _kiteInstruments.FetchExchangeInstrumentsAsync("BFO", apiKey, accessToken, cap, ct);
        var mcxTask = _kiteInstruments.FetchExchangeInstrumentsAsync("MCX", apiKey, accessToken, cap, ct);

        await Task.WhenAll(nfoTask, bfoTask, mcxTask).ConfigureAwait(false);

        var nfo = await nfoTask.ConfigureAwait(false);
        if (!nfo.Success)
            throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not load NFO instruments from Kite.");

        var fno = new List<KiteInstrumentListItemDto>(nfo.Items);
        var fnoTruncated = nfo.Truncated;

        var bfo = await bfoTask.ConfigureAwait(false);
        if (bfo.Success)
        {
            fno.AddRange(bfo.Items);
            fnoTruncated |= bfo.Truncated;
        }

        var mcx = await mcxTask.ConfigureAwait(false);
        if (!mcx.Success)
            throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not load MCX instruments from Kite.");

        return new KiteFnoCommodityListsDto(fno, mcx.Items, fnoTruncated, mcx.Truncated);
    }

    public async Task<KiteInstrumentSearchDto> SearchKiteInstrumentsAsync(
        Guid userId,
        string query,
        KiteInstrumentSearchSegment segment,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new InvalidOperationException("Search text is required.");

        var needle = query.Trim();
        if (needle.Length > KiteInstrumentSearchQueryMaxLength)
            throw new InvalidOperationException($"Search text must be at most {KiteInstrumentSearchQueryMaxLength} characters.");

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        if (segment == KiteInstrumentSearchSegment.Fno)
        {
            var cap = KiteInstrumentSearchMaxMatchesFnoPerExchange;
            var nfoTask = _kiteInstruments.SearchExchangeInstrumentsAsync("NFO", apiKey, accessToken, needle, cap, ct);
            var bfoTask = _kiteInstruments.SearchExchangeInstrumentsAsync("BFO", apiKey, accessToken, needle, cap, ct);
            await Task.WhenAll(nfoTask, bfoTask).ConfigureAwait(false);

            var nfo = await nfoTask.ConfigureAwait(false);
            if (!nfo.Success)
                throw new InvalidOperationException(nfo.ErrorMessage ?? "Could not search NFO instruments on Kite.");

            var combined = new List<KiteInstrumentListItemDto>(nfo.Items);
            var scanTruncated = nfo.Truncated;

            var bfo = await bfoTask.ConfigureAwait(false);
            if (bfo.Success)
            {
                combined.AddRange(bfo.Items);
                scanTruncated |= bfo.Truncated;
            }

            return new KiteInstrumentSearchDto(combined, scanTruncated);
        }

        {
            var mcx = await _kiteInstruments
                .SearchExchangeInstrumentsAsync(
                    "MCX",
                    apiKey,
                    accessToken,
                    needle,
                    KiteInstrumentSearchMaxMatchesMcx,
                    ct)
                .ConfigureAwait(false);
            if (!mcx.Success)
                throw new InvalidOperationException(mcx.ErrorMessage ?? "Could not search MCX instruments on Kite.");

            return new KiteInstrumentSearchDto(mcx.Items, mcx.Truncated);
        }
    }

    public async Task<KiteHistoricalCandlesDto> GetKiteHistoricalCandlesAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken) || !instrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token from Kite is required.");

        var code = NormalizeUiChartInterval(interval);
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

        var requestEnd = toUtc ?? DateTimeOffset.UtcNow;
        var requestStart = fromUtc ?? requestEnd - DefaultChartLookback(code);
        if (requestStart >= requestEnd)
            throw new InvalidOperationException("Start time must be before end time.");

        var fetchStart = ComputeMaWarmupFetchStart(requestStart, code);
        var token = instrumentToken.Trim();

        if (code is "2m" or "4m")
        {
            var period = code == "2m" ? 2 : 4;
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "minute", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeMinuteCandles(fetch.Candles, period);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        var kiteInterval = MapUiIntervalToKite(code);
        var raw = await _kiteInstruments
            .FetchHistoricalCandlesAsync(token, kiteInterval, apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
            .ConfigureAwait(false);
        if (!raw.Success)
            throw new InvalidOperationException(raw.ErrorMessage ?? "Could not load candles from Kite.");

        return FinalizeChartHistoricalCandles(raw.Candles, code, requestStart, requestEnd);
    }

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

    public async Task<KiteInstrumentsChartSettingsDto> GetKiteInstrumentsChartSettingsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var row = await _kiteChartSettings.GetAsync(userId, ct).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("User not found.");

        return new KiteInstrumentsChartSettingsDto(row.Interval, row.RangePreset, row.GraphType, ParseChartZoomMap(row.ChartZoomByInstrumentTokenJson));
    }

    public async Task SaveKiteInstrumentsChartZoomAsync(Guid userId, KiteInstrumentsChartZoomPutDto body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body.InstrumentToken) || !body.InstrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token is required.");

        var token = body.InstrumentToken.Trim();
        if (body.VisibleBars is int vb && (vb < ChartZoomMinBars || vb > ChartZoomMaxBars))
        {
            throw new InvalidOperationException(
                $"visibleBars must be between {ChartZoomMinBars} and {ChartZoomMaxBars}, or null to clear.");
        }

        var row = await _kiteChartSettings.GetAsync(userId, ct).ConfigureAwait(false);
        if (row is null)
            throw new InvalidOperationException("User not found.");

        var dict = ParseChartZoomDict(row.ChartZoomByInstrumentTokenJson);
        if (body.VisibleBars is null)
            dict.Remove(token);
        else
            dict[token] = body.VisibleBars.Value;

        var json = dict.Count == 0 ? null : JsonSerializer.Serialize(dict, ChartZoomJsonOptions);
        await _kiteChartSettings.SaveChartZoomJsonAsync(userId, json, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, int>? ParseChartZoomMap(string? json)
    {
        var d = ParseChartZoomDict(json);
        return d.Count == 0 ? null : d;
    }

    private static Dictionary<string, int> ParseChartZoomDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return d is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(d, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
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

        await _kiteChartSettings.SaveAsync(
                userId,
                new KiteInstrumentsChartSettingsState(interval, range, graph, null),
                ct)
            .ConfigureAwait(false);
    }

    private async Task RequireUserExistsAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");
    }

    private static KiteInstrumentListItemDto MapFavoriteToDto(KiteFavoriteInstrument x) =>
        new(
            x.InstrumentToken,
            x.Tradingsymbol,
            x.Exchange,
            x.Name,
            x.InstrumentType,
            x.Segment,
            x.Expiry,
            x.Strike,
            x.LotSize);

    private static string? NullableNorm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    private static string NormalizeUiChartInterval(string interval)
    {
        var t = interval.Trim().ToLowerInvariant();
        return t switch
        {
            "1m" or "2m" or "3m" or "4m" or "5m" or "10m" or "15m" or "30m" or "1h" or "1d" => t,
            _ => throw new InvalidOperationException(
                "Interval must be one of: 1m, 2m, 3m, 4m, 5m, 10m, 15m, 30m, 1h, 1d."),
        };
    }

    private static string NormalizeChartRangePreset(string preset)
    {
        var t = preset.Trim().ToLowerInvariant();
        return t switch
        {
            "auto" or "last5m" or "last10m" or "last15m" or "last30m" or "last1h" or "last5h" or "last10h"
                or "last1d" or "last2d" or "last5d" or "last1mo" => t,
            _ => throw new InvalidOperationException(
                "rangePreset must be one of: auto, last5m, last10m, last15m, last30m, last1h, last5h, last10h, last1d, last2d, last5d, last1mo."),
        };
    }

    private static string NormalizeChartGraphType(string graphType)
    {
        var t = graphType.Trim().ToLowerInvariant();
        return t switch
        {
            "line" or "bar" or "candlestick" => t,
            _ => throw new InvalidOperationException("graphType must be line, bar, or candlestick."),
        };
    }

    private static DateTimeOffset ComputeMaWarmupFetchStart(DateTimeOffset requestStart, string code)
    {
        var bar = ChartBarDuration(code);
        var delta = TimeSpan.FromTicks(bar.Ticks * ChartMovingAverages.WarmupBarCount);
        return requestStart - delta;
    }

    private static KiteHistoricalCandlesDto FinalizeChartHistoricalCandles(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        string intervalCode,
        DateTimeOffset requestStart,
        DateTimeOffset requestEnd)
    {
        var ordered = candles.OrderBy(c => c.Time).ToList();
        var withMa = ChartMovingAverages.Attach(ordered);
        var trimmed = withMa.Where(c => c.Time >= requestStart).ToList();
        return new KiteHistoricalCandlesDto(trimmed, intervalCode, requestStart, requestEnd);
    }

    /// <summary>One chart bar length for the UI interval (used to extend Kite fetch for MA warmup).</summary>
    private static TimeSpan ChartBarDuration(string code) =>
        code switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "2m" => TimeSpan.FromMinutes(2),
            "3m" => TimeSpan.FromMinutes(3),
            "4m" => TimeSpan.FromMinutes(4),
            "5m" => TimeSpan.FromMinutes(5),
            "10m" => TimeSpan.FromMinutes(10),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "1d" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(5),
        };

    private static TimeSpan DefaultChartLookback(string code) =>
        code switch
        {
            "1m" or "2m" or "4m" => TimeSpan.FromDays(5),
            "3m" => TimeSpan.FromDays(30),
            "5m" => TimeSpan.FromDays(60),
            "10m" => TimeSpan.FromDays(90),
            "15m" => TimeSpan.FromDays(120),
            "30m" => TimeSpan.FromDays(180),
            "1h" => TimeSpan.FromDays(365),
            "1d" => TimeSpan.FromDays(730),
            _ => TimeSpan.FromDays(5),
        };

    private static string MapUiIntervalToKite(string code) =>
        code switch
        {
            "1m" => "minute",
            "3m" => "3minute",
            "5m" => "5minute",
            "10m" => "10minute",
            "15m" => "15minute",
            "30m" => "30minute",
            "1h" => "60minute",
            "1d" => "day",
            _ => throw new InvalidOperationException("Unsupported interval."),
        };

    private static IReadOnlyList<KiteHistoricalCandlePointDto> MergeMinuteCandles(
        IReadOnlyList<KiteHistoricalCandlePointDto> minutes,
        int periodMinutes)
    {
        if (minutes.Count == 0 || periodMinutes <= 1)
            return minutes;

        var ordered = minutes.OrderBy(c => c.Time).ToList();
        var merged = new List<KiteHistoricalCandlePointDto>();
        long? bucketKey = null;
        decimal open = 0, high = 0, low = 0, close = 0;
        long volume = 0;
        var haveOpen = false;

        void Flush()
        {
            if (!haveOpen || bucketKey is null)
                return;

            merged.Add(new KiteHistoricalCandlePointDto(
                DateTimeOffset.FromUnixTimeSeconds(bucketKey.Value),
                open,
                high,
                low,
                close,
                volume));
        }

        foreach (var c in ordered)
        {
            var secs = c.Time.ToUnixTimeSeconds();
            var key = secs - secs % (periodMinutes * 60L);
            if (bucketKey != key)
            {
                Flush();
                bucketKey = key;
                open = c.Open;
                high = c.High;
                low = c.Low;
                close = c.Close;
                volume = c.Volume;
                haveOpen = true;
            }
            else
            {
                high = Math.Max(high, c.High);
                low = Math.Min(low, c.Low);
                close = c.Close;
                volume += c.Volume;
            }
        }

        Flush();
        return merged;
    }

    private async Task<(string ApiKey, string AccessToken)> RequireKiteInstrumentSessionAsync(
        Guid userId,
        CancellationToken ct)
    {
        var snapshot = await _brokerSetup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (snapshot is null)
            throw new InvalidOperationException("User not found.");

        if (!string.Equals(snapshot.BrokerProvider, "Zerodha", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Zerodha (Kite) is not connected.");

        var accessToken = await _brokerSetup.GetKiteAccessTokenAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("No valid Kite session. Reconnect Zerodha.");

        var apiKey = _kiteOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Zerodha Kite is not configured. Set environment variable ZerodhaKite__ApiKey (see README).");

        return (apiKey, accessToken);
    }

    /// <summary>Maps callback <paramref name="state"/> to the HMAC payload. Returns the server cache key when resolved from memory (for one-time removal).</summary>
    private (string SignedState, string? PendingKey) ResolveKiteOAuthState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return (state, null);

        var trimmed = state.Trim();

        if (trimmed.Length == 32
            && trimmed.All(char.IsAsciiHexDigit)
            && _memoryCache.TryGetValue(
                PendingStateCachePrefix + trimmed.ToLowerInvariant(),
                out var cached)
            && cached is string full
            && !string.IsNullOrEmpty(full))
        {
            return (full, trimmed.ToLowerInvariant());
        }

        return (trimmed, null);
    }
}
