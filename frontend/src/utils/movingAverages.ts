import type { ChartPointOhlc } from './liveCandleMerge'

/** Classic overlay periods (match toolbar interval; same on every chart). */
export const MA_SMA_PERIOD = 20
export const MA_EMA_FAST_PERIOD = 9
export const MA_EMA_SLOW_PERIOD = 21

export type ChartPointWithMa = ChartPointOhlc & {
  sma20: number | null
  ema9: number | null
  ema21: number | null
}

/** Stroke colors aligned across Recharts and SVG candlestick. */
export const MA_LINE_COLORS = {
  sma20: '#fbbf24',
  ema9: '#a78bfa',
  ema21: '#38bdf8',
} as const

export function computeSma(values: readonly number[], period: number): (number | null)[] {
  const out: (number | null)[] = values.map(() => null)
  if (period < 1 || values.length < period) return out

  for (let i = period - 1; i < values.length; i++) {
    let s = 0
    for (let j = 0; j < period; j++) s += values[i - j]
    out[i] = s / period
  }
  return out
}

/** EMA seeded with SMA at index <c>period - 1</c>, then standard smoothing for later bars. */
export function computeEma(values: readonly number[], period: number): (number | null)[] {
  const out: (number | null)[] = values.map(() => null)
  if (period < 1 || values.length < period) return out

  let ema = 0
  for (let i = 0; i < period; i++) ema += values[i]
  ema /= period

  const k = 2 / (period + 1)
  out[period - 1] = ema

  for (let i = period; i < values.length; i++) {
    ema = values[i] * k + ema * (1 - k)
    out[i] = ema
  }
  return out
}

export function attachMovingAverages(points: ChartPointOhlc[]): ChartPointWithMa[] {
  if (points.length === 0) return []

  const closes = points.map((p) => p.close)
  const sma20 = computeSma(closes, MA_SMA_PERIOD)
  const ema9 = computeEma(closes, MA_EMA_FAST_PERIOD)
  const ema21 = computeEma(closes, MA_EMA_SLOW_PERIOD)

  return points.map((p, i) => ({
    ...p,
    sma20: sma20[i],
    ema9: ema9[i],
    ema21: ema21[i],
  }))
}
