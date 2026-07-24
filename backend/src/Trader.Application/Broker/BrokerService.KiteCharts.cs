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
    public async Task<KiteHistoricalCandlesDto> GetKiteHistoricalCandlesAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        return await GetOrComposeChartHistoricalCandlesCachedAsync(userId, instrumentToken, interval, fromUtc, toUtc, ct)
            .ConfigureAwait(false);
    }

    public async Task<KiteHistoricalOhlcvOnlyDto> GetKiteHistoricalChartOhlcvAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        var full = await GetOrComposeChartHistoricalCandlesCachedAsync(userId, instrumentToken, interval, fromUtc, toUtc, ct)
            .ConfigureAwait(false);
        return ProjectToOhlcvOnly(full);
    }

    public async Task<KiteHistoricalOverlaysDto> GetKiteHistoricalChartOverlaysAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct = default)
    {
        var full = await GetOrComposeChartHistoricalCandlesCachedAsync(userId, instrumentToken, interval, fromUtc, toUtc, ct)
            .ConfigureAwait(false);
        return ProjectToOverlays(full);
    }

    public async Task<KiteInstrumentLiveQuoteDto> GetKiteInstrumentLiveQuoteAsync(
        Guid userId,
        string exchange,
        string tradingsymbol,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(tradingsymbol))
            throw new InvalidOperationException("exchange and tradingsymbol are required.");

        var ex = exchange.Trim().ToUpperInvariant();
        var ts = tradingsymbol.Trim();
        var iq = $"{ex}:{ts}";
        var cacheKey = $"Trader.LiveQuote:v1:{userId:N}:{ex}:{ts}";
        if (_memoryCache.TryGetValue(cacheKey, out KiteInstrumentLiveQuoteDto? cachedQuote) && cachedQuote is not null)
            return cachedQuote;

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var fetch = await _kiteInstruments.FetchQuoteOhlcAsync([iq], apiKey, accessToken, ct).ConfigureAwait(false);
        if (!fetch.Success || fetch.ByKey is null || !fetch.ByKey.TryGetValue(iq, out var tick))
        {
            throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load live quote.");
        }

        var dto = new KiteInstrumentLiveQuoteDto(ex, ts, tick.LastPrice, tick.OhlcClose);
        _memoryCache.Set(
            cacheKey,
            dto,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = LiveQuoteCacheTtl });
        return dto;
    }
    private async Task<KiteHistoricalCandlesDto> GetOrComposeChartHistoricalCandlesCachedAsync(
        Guid userId,
        string instrumentToken,
        string interval,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentToken) || !instrumentToken.Trim().All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token from Kite is required.");

        var token = instrumentToken.Trim();
        var code = NormalizeUiChartInterval(interval);
        // Open-ended "to" must be stable across parallel OHLC + overlay requests (and
        // near-simultaneous polls); otherwise each call gets a distinct cache key / From-To.
        var requestEnd = toUtc ?? SnapOpenEndedChartEnd(DateTimeOffset.UtcNow);
        var requestStart = fromUtc ?? requestEnd - DefaultChartLookback(code);
        if (requestStart >= requestEnd)
            throw new InvalidOperationException("Start time must be before end time.");

        var fetchStart = ComputeMaWarmupFetchStart(requestStart, code);
        var cacheKey = $"Trader.ChartHist:v2:{userId:N}:{token}:{code}:{fetchStart.UtcTicks}:{requestEnd.UtcTicks}";
        if (_memoryCache.TryGetValue(cacheKey, out KiteHistoricalCandlesDto? hit) && hit is not null)
            return hit;

        var dto = await FetchHistoricalCandlesFreshAsync(userId, token, code, requestStart, requestEnd, fetchStart, ct)
            .ConfigureAwait(false);
        _memoryCache.Set(
            cacheKey,
            dto,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChartHistoricalCacheTtl });
        return dto;
    }

    private async Task<KiteHistoricalCandlesDto> FetchHistoricalCandlesFreshAsync(
        Guid userId,
        string token,
        string code,
        DateTimeOffset requestStart,
        DateTimeOffset requestEnd,
        DateTimeOffset fetchStart,
        CancellationToken ct)
    {
        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);

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

        if (code is "2h" or "4h" or "8h")
        {
            var bucketHours = code switch
            {
                "2h" => 2L,
                "4h" => 4L,
                "8h" => 8L,
                _ => 4L,
            };
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "60minute", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeOhlcByBucketSeconds(fetch.Candles, bucketHours * 3600);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        if (code == "90m")
        {
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "minute", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeOhlcByBucketSeconds(fetch.Candles, 90L * 60);
            return FinalizeChartHistoricalCandles(merged, code, requestStart, requestEnd);
        }

        if (code == "1w")
        {
            var fetch = await _kiteInstruments
                .FetchHistoricalCandlesAsync(token, "day", apiKey, accessToken, fetchStart, requestEnd, continuous: false, ct)
                .ConfigureAwait(false);
            if (!fetch.Success)
                throw new InvalidOperationException(fetch.ErrorMessage ?? "Could not load candles from Kite.");

            var merged = MergeEveryNCandles(fetch.Candles, 7);
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

    private static KiteHistoricalOhlcvOnlyDto ProjectToOhlcvOnly(KiteHistoricalCandlesDto dto)
    {
        var candles = dto.Candles
            .Select(c => new KiteHistoricalOhlcvOnlyCandleDto(c.Time, c.Open, c.High, c.Low, c.Close, c.Volume))
            .ToList();
        return new KiteHistoricalOhlcvOnlyDto(candles, dto.Interval, dto.From, dto.To);
    }

    private static KiteHistoricalOverlaysDto ProjectToOverlays(KiteHistoricalCandlesDto dto)
    {
        var pts = dto.Candles
            .Select(c => new KiteHistoricalOverlayPointDto(
                c.Time,
                c.Sma20,
                c.Ema9,
                c.Ema21,
                c.SrSupport,
                c.SrResistance))
            .ToList();
        return new KiteHistoricalOverlaysDto(pts, dto.Interval, dto.From, dto.To);
    }
    private static string NormalizeChartGraphType(string graphType)
    {
        var t = graphType.Trim().ToLowerInvariant();
        return t switch
        {
            "line" or "bar" or "candlestick" or "trend" => t,
            _ => throw new InvalidOperationException("graphType must be line, bar, candlestick, or trend."),
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

    /// <summary>
    /// Floor UTC "now" into a short bucket so concurrent chart split endpoints share one window.
    /// Bucket length matches <see cref="ChartHistoricalCacheTtl"/> so cache keys stay coherent.
    /// </summary>
    private static DateTimeOffset SnapOpenEndedChartEnd(DateTimeOffset utcNow)
    {
        var bucketTicks = ChartHistoricalCacheTtl.Ticks;
        if (bucketTicks < TimeSpan.TicksPerSecond)
            bucketTicks = TimeSpan.TicksPerSecond;
        var ticks = utcNow.UtcTicks - (utcNow.UtcTicks % bucketTicks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>One chart bar length for the UI interval (used to extend Kite fetch for MA warmup).</summary>
    private static TimeSpan ChartBarDuration(string code) => ChartUiIntervals.BarDuration(code);

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
            "90m" => TimeSpan.FromDays(365),
            "2h" => TimeSpan.FromDays(540),
            "4h" => TimeSpan.FromDays(730),
            "8h" => TimeSpan.FromDays(1095),
            "1d" => TimeSpan.FromDays(730),
            "1w" => TimeSpan.FromDays(365 * 12),
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
        return MergeOhlcByBucketSeconds(ordered, periodMinutes * 60L);
    }

    private static IReadOnlyList<KiteHistoricalCandlePointDto> MergeOhlcByBucketSeconds(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        long bucketSeconds)
    {
        if (candles.Count == 0 || bucketSeconds < 1)
            return candles;

        var ordered = candles.OrderBy(c => c.Time).ToList();
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
            var key = secs - secs % bucketSeconds;
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

    /// <summary>Group consecutive daily bars into blocks of <paramref name="n"/> (7 → ~weekly bars for UI <c>1w</c>).</summary>
    private static IReadOnlyList<KiteHistoricalCandlePointDto> MergeEveryNCandles(
        IReadOnlyList<KiteHistoricalCandlePointDto> candles,
        int n)
    {
        if (n <= 1 || candles.Count == 0)
            return candles;

        var ordered = candles.OrderBy(c => c.Time).ToList();
        var merged = new List<KiteHistoricalCandlePointDto>();
        for (var i = 0; i < ordered.Count; i += n)
        {
            var chunk = ordered.Skip(i).Take(n).ToList();
            if (chunk.Count == 0)
                break;

            var o = chunk[0].Open;
            var hi = chunk.Max(x => x.High);
            var lo = chunk.Min(x => x.Low);
            var last = chunk[^1];
            var vol = chunk.Sum(x => x.Volume);
            merged.Add(new KiteHistoricalCandlePointDto(last.Time, o, hi, lo, last.Close, vol));
        }

        return merged;
    }
}
