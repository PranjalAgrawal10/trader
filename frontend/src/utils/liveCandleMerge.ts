import type { MarketTickBatchItem } from '../services/marketHub'

/** Chart intervals matching Kite instruments page. */
export type ChartIntervalKey =
  | '1m'
  | '2m'
  | '3m'
  | '4m'
  | '5m'
  | '10m'
  | '15m'
  | '30m'
  | '1h'
  | '4h'
  | '1d'
  | '1w'

export type ChartPointOhlc = {
  idx: number
  t: string
  open: number
  high: number
  low: number
  close: number
  volume: number
  ohlc: string
}

type ChartGraph = 'line' | 'bar' | 'candlestick'

/** Kite tick `v` is session cumulative volume — track it to add per-tick deltas onto the in-progress bar (see backend `LiveCandleTickSubscriber.ApplyTick`). */
export type LiveTickVolumeAccumulator = { lastCumulativeVolume: number | null }

function formatOhlc(o: number, h: number, l: number, c: number, v: number): string {
  return `O ${o}  H ${h}  L ${l}  C ${c}  V ${v}`
}

function applyKiteCumulativeVolumeDelta(
  barVolume: number,
  tickCumulative: number,
  state: LiveTickVolumeAccumulator,
): number {
  if (!Number.isFinite(tickCumulative) || tickCumulative < 0) return barVolume
  const cur = Number.isFinite(barVolume) && barVolume >= 0 ? barVolume : 0
  if (state.lastCumulativeVolume == null) {
    state.lastCumulativeVolume = tickCumulative
    return cur
  }
  const prev = state.lastCumulativeVolume
  const delta = tickCumulative >= prev ? tickCumulative - prev : 0
  state.lastCumulativeVolume = tickCumulative
  return cur + delta
}

export function intervalToMs(interval: ChartIntervalKey): number {
  switch (interval) {
    case '1m':
      return 60_000
    case '2m':
      return 120_000
    case '3m':
      return 180_000
    case '4m':
      return 240_000
    case '5m':
      return 300_000
    case '10m':
      return 600_000
    case '15m':
      return 900_000
    case '30m':
      return 1_800_000
    case '1h':
      return 3_600_000
    case '4h':
      return 14_400_000
    case '1d':
      return 86_400_000
    case '1w':
      return 7 * 86_400_000
    default:
      return 60_000
  }
}

/** Candle bucket open time in UTC ms (aligned with Kite candle `time` as bar open). */
export function candleBucketStartUtc(ms: number, interval: ChartIntervalKey): number {
  if (interval === '1d') {
    const d = new Date(ms)
    return Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())
  }
  if (interval === '1w') {
    const step = intervalToMs('1w')
    return Math.floor(ms / step) * step
  }
  const step = intervalToMs(interval)
  return Math.floor(ms / step) * step
}

/**
 * Merges the latest SignalR tick into the trailing OHLC series (live, in-progress bar) for candles, line, and bar charts.
 * When `volumeAccumulator` is supplied, bar {@link ChartPointOhlc.volume} is updated using Kite cumulative session volume deltas.
 */
export function mergeLiveTickIntoOhlc(
  series: ChartPointOhlc[],
  lastTick: MarketTickBatchItem | null,
  interval: ChartIntervalKey,
  _graphType: ChartGraph,
  volumeAccumulator?: LiveTickVolumeAccumulator,
): ChartPointOhlc[] {
  if (!lastTick || series.length === 0) return series

  const price = lastTick.p
  const tickVol = lastTick.v
  const tickMs = lastTick.t != null ? lastTick.t * 1000 : Date.now()
  const tickBucket = candleBucketStartUtc(tickMs, interval)

  const last = series[series.length - 1]
  const lastMs = new Date(last.t).getTime()
  const lastBucket = candleBucketStartUtc(lastMs, interval)

  if (tickBucket < lastBucket) {
    if (volumeAccumulator && Number.isFinite(tickVol) && tickVol >= 0) {
      volumeAccumulator.lastCumulativeVolume = tickVol
    }
    return series
  }

  if (tickBucket === lastBucket) {
    const hi = Math.max(last.high, price)
    const lo = Math.min(last.low, price)
    const nextVol =
      volumeAccumulator != null
        ? applyKiteCumulativeVolumeDelta(last.volume, tickVol, volumeAccumulator)
        : last.volume
    const updated: ChartPointOhlc = {
      ...last,
      high: hi,
      low: lo,
      close: price,
      volume: nextVol,
      ohlc: formatOhlc(last.open, hi, lo, price, nextVol),
    }
    return [...series.slice(0, -1), updated]
  }

  const ncVolBase = 0
  const ncVol =
    volumeAccumulator != null ? applyKiteCumulativeVolumeDelta(ncVolBase, tickVol, volumeAccumulator) : 0
  const nc: ChartPointOhlc = {
    idx: last.idx + 1,
    t: new Date(tickBucket).toISOString(),
    open: last.close,
    high: Math.max(last.close, price),
    low: Math.min(last.close, price),
    close: price,
    volume: ncVol,
    ohlc: formatOhlc(last.close, Math.max(last.close, price), Math.min(last.close, price), price, ncVol),
  }
  return [...series, nc]
}
