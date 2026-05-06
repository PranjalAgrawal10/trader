using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Configuration;
using Trader.Application.Streaming;

namespace Trader.Infrastructure.Streaming;

/// <summary>
/// Builds UTC 1-minute OHLC bars from Kite ticks (LTP mode). One series per <see cref="MarketTickDto.InstrumentToken"/> so duplicate user streams do not double-write.
/// Finalized bars are upserted into <c>HistoricalCandles</c>.
/// </summary>
public sealed class LiveCandleTickSubscriber : IDisposable
{
    private static readonly TimeSpan StaleFlushInterval = TimeSpan.FromSeconds(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<LiveCandlesOptions> _options;
    private readonly ILogger<LiveCandleTickSubscriber> _logger;
    private readonly ConcurrentDictionary<uint, OpenBarState> _perInstrument = new();
    private readonly Timer _staleTimer;

    public LiveCandleTickSubscriber(
        IServiceScopeFactory scopeFactory,
        IOptions<LiveCandlesOptions> options,
        ILogger<LiveCandleTickSubscriber> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _staleTimer = new Timer(_ => FlushStaleBars(DateTimeOffset.UtcNow), null, StaleFlushInterval, StaleFlushInterval);
    }

    public void OnTick(Guid userId, MarketTickDto tick)
    {
        _ = userId;
        if (!_options.Value.Enabled)
            return;

        var t = TickTimestampUtc(tick);
        var bucketStart = FloorUtcMinute(t);

        var state = _perInstrument.GetOrAdd(tick.InstrumentToken, static _ => new OpenBarState());
        OpenBar? toPersist = null;
        uint token = tick.InstrumentToken;

        lock (state.Gate)
        {
            if (state.Current is { } cur && cur.BucketStartUtc != bucketStart)
            {
                toPersist = cur;
                state.Current = null;
            }

            if (state.Current is null)
                state.Current = NewBar(bucketStart, tick);
            else
                ApplyTick(state.Current, tick);
        }

        if (toPersist is not null)
            QueuePersist(toPersist, token);
    }

    /// <summary>Closes bars when the clock has entered a newer minute but no tick arrived yet.</summary>
    public void FlushStaleBars(DateTimeOffset utcNow)
    {
        if (!_options.Value.Enabled)
            return;

        var boundary = FloorUtcMinute(utcNow);
        foreach (var kv in _perInstrument.ToArray())
        {
            OpenBar? done = null;
            var token = kv.Key;
            lock (kv.Value.Gate)
            {
                if (kv.Value.Current is { } cur && cur.BucketStartUtc < boundary)
                {
                    done = cur;
                    kv.Value.Current = null;
                }
            }

            if (done is not null)
                QueuePersist(done, token);
        }
    }

    private void QueuePersist(OpenBar bar, uint instrumentToken)
    {
        var tokenStr = instrumentToken.ToString(CultureInfo.InvariantCulture);
        var timeframe = "1m";
        var bucket = bar.BucketStartUtc;
        var o = bar.Open;
        var h = bar.High;
        var l = bar.Low;
        var c = bar.Close;
        var v = bar.VolumeSum;
        _ = PersistSafelyAsync(tokenStr, timeframe, bucket, o, h, l, c, v);
    }

    private async Task PersistSafelyAsync(
        string instrumentToken,
        string timeframe,
        DateTimeOffset timestampUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var upserter = scope.ServiceProvider.GetRequiredService<IHistoricalCandleUpserter>();
            await upserter
                .UpsertAsync(instrumentToken, timeframe, timestampUtc, open, high, low, close, volume)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist live {Timeframe} candle for instrument {Instrument} at {OpenUtc:O}",
                timeframe,
                instrumentToken,
                timestampUtc);
        }
    }

    private static DateTimeOffset TickTimestampUtc(MarketTickDto tick)
    {
        if (tick.UnixTimestampSeconds is { } sec)
            return DateTimeOffset.FromUnixTimeSeconds(sec);
        return DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset FloorUtcMinute(DateTimeOffset t)
    {
        var u = t.ToUniversalTime();
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, u.Minute, 0, TimeSpan.Zero);
    }

    private static OpenBar NewBar(DateTimeOffset bucketStart, MarketTickDto tick)
    {
        var p = tick.LastPrice;
        return new OpenBar
        {
            BucketStartUtc = bucketStart,
            Open = p,
            High = p,
            Low = p,
            Close = p,
            VolumeSum = 0,
            LastCumulativeVolume = tick.Volume,
        };
    }

    private static void ApplyTick(OpenBar b, MarketTickDto tick)
    {
        var p = tick.LastPrice;
        b.High = Math.Max(b.High, p);
        b.Low = Math.Min(b.Low, p);
        b.Close = p;

        var cum = tick.Volume;
        if (cum >= b.LastCumulativeVolume)
            b.VolumeSum += (long)(cum - b.LastCumulativeVolume);
        b.LastCumulativeVolume = cum;
    }

    public void Dispose() => _staleTimer.Dispose();

    private sealed class OpenBarState
    {
        public object Gate { get; } = new();
        public OpenBar? Current;
    }

    private sealed class OpenBar
    {
        public required DateTimeOffset BucketStartUtc { get; init; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long VolumeSum { get; set; }
        public uint LastCumulativeVolume { get; set; }
    }
}
