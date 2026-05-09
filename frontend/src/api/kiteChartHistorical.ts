import { api } from './client'

/** Matches combined `historical-candles` shape — built from parallel OHLC + overlay splits. */
export interface HistoricalChartCandlesResponse {
  candles: {
    time: string
    open: number
    high: number
    low: number
    close: number
    volume: number
    sma20?: number | null
    ema9?: number | null
    ema21?: number | null
    srSupport?: number | null
    srResistance?: number | null
  }[]
  interval: string
  from: string
  to: string
}

interface OhlcOnlyResponse {
  candles: {
    time: string
    open: number
    high: number
    low: number
    close: number
    volume: number
  }[]
  interval: string
  from: string
  to: string
}

export interface OhlcOnlyMultiResponse {
  items: OhlcOnlyResponse[]
}

interface OverlaysOnlyResponse {
  points: {
    time: string
    sma20?: number | null
    ema9?: number | null
    ema21?: number | null
    srSupport?: number | null
    srResistance?: number | null
  }[]
  interval: string
  from: string
  to: string
}

function mergeHistoricalSlices(ohlc: OhlcOnlyResponse, overlays: OverlaysOnlyResponse): HistoricalChartCandlesResponse {
  if (ohlc.interval !== overlays.interval || ohlc.from !== overlays.from || ohlc.to !== overlays.to)
    throw new Error('OHLC vs overlay mismatch (interval/from/to). Retry with the same Range query.')

  const byOvTime = new Map(
    overlays.points.map((p) => [normalizeChartTimeLabel(p.time), p] as const),
  )

  const candles = ohlc.candles.map((c) => {
    const ov = byOvTime.get(normalizeChartTimeLabel(c.time))
    return {
      time: c.time,
      open: c.open,
      high: c.high,
      low: c.low,
      close: c.close,
      volume: c.volume,
      sma20: ov?.sma20 ?? null,
      ema9: ov?.ema9 ?? null,
      ema21: ov?.ema21 ?? null,
      srSupport: ov?.srSupport ?? null,
      srResistance: ov?.srResistance ?? null,
    }
  })

  return { candles, interval: ohlc.interval, from: ohlc.from, to: ohlc.to }
}

function normalizeChartTimeLabel(t: string): string {
  const d = new Date(t).getTime()
  return Number.isFinite(d) ? String(d) : t
}

/** Parallel OHLC + overlay endpoints — server composites share ~25s cache for low duplicate Kite work. */
export async function fetchMergedHistoricalChartCandles(
  instrumentToken: string,
  interval: string,
  rangeQuery: Record<string, string | undefined>,
  signal?: AbortSignal,
): Promise<HistoricalChartCandlesResponse> {
  const params = { instrumentToken, interval, ...rangeQuery }
  const [ohRes, ovRes] = await Promise.all([
    api.get<OhlcOnlyResponse>('/broker/kite/chart/historical-ohlc', { params, signal }),
    api.get<OverlaysOnlyResponse>('/broker/kite/chart/historical-overlays', { params, signal }),
  ])
  return mergeHistoricalSlices(ohRes.data, ovRes.data)
}

export async function fetchHistoricalChartOhlcMulti(
  instrumentToken: string,
  intervals: readonly string[],
  rangeQuery: Record<string, string | undefined>,
  signal?: AbortSignal,
): Promise<OhlcOnlyMultiResponse> {
  const cleaned = intervals.map((x) => x.trim()).filter((x) => x.length > 0)
  if (cleaned.length === 0) return { items: [] }
  const params = { instrumentToken, intervals: cleaned.join(','), ...rangeQuery }
  const { data } = await api.get<OhlcOnlyMultiResponse>('/broker/kite/chart/historical-ohlc/multi', { params, signal })
  return data
}
