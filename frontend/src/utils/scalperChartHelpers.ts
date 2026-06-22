import type { HistoricalChartCandlesResponse } from '../api/kiteChartHistorical'
import type { MarketTickBatchItem } from '../services/marketHub'
import {
  mergeLiveTickIntoOhlc,
  type ChartIntervalKey,
  type ChartPointOhlc,
  type LiveTickVolumeAccumulator,
} from './liveCandleMerge'
import {
  addCustomEmaToChartPoints,
  attachMovingAverages,
  type ChartPointWithMa,
  type MaLineVisibility,
} from './movingAverages'

export type ScalperInterval = Extract<ChartIntervalKey, '1m' | '3m' | '5m'>
export type ScalperRange = 'last15m' | 'last30m' | 'last1h' | 'last5h' | 'last1d' | 'last3d'

export const SCALPER_INTERVALS: ScalperInterval[] = ['1m', '3m', '5m']
export const SCALPER_RANGES: { id: ScalperRange; label: string }[] = [
  { id: 'last15m', label: '15m' },
  { id: 'last30m', label: '30m' },
  { id: 'last1h', label: '1h' },
  { id: 'last5h', label: '5h' },
  { id: 'last1d', label: '1d' },
  { id: 'last3d', label: '3d' },
]

export const SCALPER_POLL_MS = 15_000

export const SCALPER_MA: MaLineVisibility = {
  showSma20: false,
  showEma9: true,
  showEma21: true,
  showCustomEma: false,
  showSupportResistance: true,
  showLinearCloseTrend: false,
}

export function scalperRangeQueryParams(preset: ScalperRange): { from: string; to: string } {
  const to = new Date()
  const from = new Date(to.getTime())
  switch (preset) {
    case 'last15m':
      from.setUTCMinutes(from.getUTCMinutes() - 15)
      break
    case 'last30m':
      from.setUTCMinutes(from.getUTCMinutes() - 30)
      break
    case 'last1h':
      from.setUTCHours(from.getUTCHours() - 1)
      break
    case 'last5h':
      from.setUTCHours(from.getUTCHours() - 5)
      break
    case 'last1d':
      from.setUTCDate(from.getUTCDate() - 1)
      break
    case 'last3d':
      from.setUTCDate(from.getUTCDate() - 3)
      break
  }
  return { from: from.toISOString(), to: to.toISOString() }
}

export function historicalCandlesToPoints(data: HistoricalChartCandlesResponse): ChartPointOhlc[] {
  return data.candles.map((c, idx) => ({
    idx: idx + 1,
    t: c.time,
    open: Number(c.open),
    high: Number(c.high),
    low: Number(c.low),
    close: Number(c.close),
    volume: Number(c.volume),
    ohlc: `O ${c.open}  H ${c.high}  L ${c.low}  C ${c.close}  V ${c.volume}`,
  }))
}

export function chartPointsFromHistorical(data: HistoricalChartCandlesResponse): ChartPointWithMa[] {
  const pts = historicalCandlesToPoints(data)
  const serverMa =
    data.candles.length === pts.length &&
    data.candles.some((c) => c.sma20 != null || c.ema9 != null || c.ema21 != null)
  const base: ChartPointWithMa[] = !serverMa
    ? attachMovingAverages(pts)
    : pts.map((p, i) => ({
        ...p,
        sma20: data.candles[i].sma20 != null ? Number(data.candles[i].sma20) : null,
        ema9: data.candles[i].ema9 != null ? Number(data.candles[i].ema9) : null,
        ema21: data.candles[i].ema21 != null ? Number(data.candles[i].ema21) : null,
        emaCustom: null,
        srSupport: data.candles[i].srSupport != null ? Number(data.candles[i].srSupport) : null,
        srResistance: data.candles[i].srResistance != null ? Number(data.candles[i].srResistance) : null,
      }))
  return addCustomEmaToChartPoints(base, null)
}

export function mergeScalperLiveIntoSeries(
  rawSeries: ChartPointWithMa[],
  liveTick: MarketTickBatchItem | null,
  interval: ScalperInterval,
  volumeAccumulator?: LiveTickVolumeAccumulator,
): ChartPointWithMa[] {
  if (rawSeries.length === 0) return []
  const tickMerged = mergeLiveTickIntoOhlc(
    rawSeries as ChartPointOhlc[],
    liveTick,
    interval,
    'candlestick',
    volumeAccumulator,
  )
  return addCustomEmaToChartPoints(attachMovingAverages(tickMerged), null)
}

export function pctChange(prev: number, next: number): string {
  if (!Number.isFinite(prev) || prev === 0 || !Number.isFinite(next)) return '—'
  const p = ((next - prev) / prev) * 100
  return p >= 0 ? `+${p.toFixed(2)}%` : `${p.toFixed(2)}%`
}

export function roundScalperPrice(price: number): number {
  return Number(price.toFixed(2))
}

export interface ScalperLiveQuoteSnapshot {
  lastPrice: number | null
  tickPrice: number | null
}

/** Price anchor at order submit — before the broker round-trip can move LTP. */
export function snapshotScalperSubmitReferencePrice(
  normalizedPrice: number | null,
  normalizedTrigger: number | null,
  orderType: 'MARKET' | 'LIMIT' | 'SL' | 'SL-M',
  liveQuote: ScalperLiveQuoteSnapshot,
  lastBarClose: number | null,
): number | null {
  if (normalizedPrice != null && Number.isFinite(normalizedPrice)) return roundScalperPrice(normalizedPrice)
  if (orderType === 'SL-M' && normalizedTrigger != null && Number.isFinite(normalizedTrigger)) {
    return roundScalperPrice(normalizedTrigger)
  }
  if (liveQuote.tickPrice != null && Number.isFinite(liveQuote.tickPrice)) {
    return roundScalperPrice(liveQuote.tickPrice)
  }
  if (liveQuote.lastPrice != null && Number.isFinite(liveQuote.lastPrice)) {
    return roundScalperPrice(liveQuote.lastPrice)
  }
  if (lastBarClose != null && Number.isFinite(lastBarClose)) return roundScalperPrice(lastBarClose)
  return null
}

export interface ScalperKiteOrderFillRow {
  orderId: string
  status: string
  averagePrice: number | null
  price: number | null
  filledQuantity: number
}

const KITE_ORDER_TERMINAL_STATUSES = new Set(['COMPLETE', 'CANCELLED', 'REJECTED'])

/** Poll Kite orderbook briefly for average fill price; falls back to submit reference. */
export async function resolveKiteOrderExecutionPrice(
  orderId: string,
  fallbackPrice: number,
  listOrders: () => Promise<readonly ScalperKiteOrderFillRow[]>,
): Promise<number> {
  for (let attempt = 0; attempt < 8; attempt++) {
    if (attempt > 0) await new Promise((resolve) => window.setTimeout(resolve, 350))
    try {
      const items = await listOrders()
      const row = items.find((o) => o.orderId === orderId)
      if (!row) continue
      if (
        row.averagePrice != null &&
        row.averagePrice > 0 &&
        row.filledQuantity > 0 &&
        Number.isFinite(row.averagePrice)
      ) {
        return roundScalperPrice(row.averagePrice)
      }
      const status = row.status.trim().toUpperCase()
      if (KITE_ORDER_TERMINAL_STATUSES.has(status)) {
        if (row.price != null && row.price > 0 && Number.isFinite(row.price)) {
          return roundScalperPrice(row.price)
        }
        break
      }
    } catch {
      /* retry */
    }
  }
  return roundScalperPrice(fallbackPrice)
}
