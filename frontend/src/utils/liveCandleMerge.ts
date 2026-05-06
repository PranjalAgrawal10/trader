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
  | '1d'

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

function formatOhlc(o: number, h: number, l: number, c: number, v: number): string {
  return `O ${o}  H ${h}  L ${l}  C ${c}  V ${v}`
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
    case '1d':
      return 86_400_000
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
  const step = intervalToMs(interval)
  return Math.floor(ms / step) * step
}

/**
 * Merges the latest SignalR tick into the trailing OHLC series for candlestick view (live, in-progress bar).
 */
export function mergeLiveTickIntoOhlc(
  series: ChartPointOhlc[],
  lastTick: MarketTickBatchItem | null,
  interval: ChartIntervalKey,
  graphType: ChartGraph,
): ChartPointOhlc[] {
  if (graphType !== 'candlestick' || !lastTick || series.length === 0) return series

  const price = lastTick.p
  const tickMs = lastTick.t != null ? lastTick.t * 1000 : Date.now()
  const tickBucket = candleBucketStartUtc(tickMs, interval)

  const last = series[series.length - 1]
  const lastMs = new Date(last.t).getTime()
  const lastBucket = candleBucketStartUtc(lastMs, interval)

  if (tickBucket < lastBucket) return series

  if (tickBucket === lastBucket) {
    const hi = Math.max(last.high, price)
    const lo = Math.min(last.low, price)
    const updated: ChartPointOhlc = {
      ...last,
      high: hi,
      low: lo,
      close: price,
      ohlc: formatOhlc(last.open, hi, lo, price, last.volume),
    }
    return [...series.slice(0, -1), updated]
  }

  const nc: ChartPointOhlc = {
    idx: last.idx + 1,
    t: new Date(tickBucket).toISOString(),
    open: last.close,
    high: Math.max(last.close, price),
    low: Math.min(last.close, price),
    close: price,
    volume: 0,
    ohlc: formatOhlc(last.close, Math.max(last.close, price), Math.min(last.close, price), price, 0),
  }
  return [...series, nc]
}
