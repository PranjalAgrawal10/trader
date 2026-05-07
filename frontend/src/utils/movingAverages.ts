import type { ChartPointOhlc } from './liveCandleMerge'

/** Classic overlay periods (match toolbar interval; same on every chart). */
export const MA_SMA_PERIOD = 20
export const MA_EMA_FAST_PERIOD = 9
export const MA_EMA_SLOW_PERIOD = 21

/** Rolling swing support (min low) / resistance (max high) over this many bars. */
export const SR_SWING_PERIOD = MA_SMA_PERIOD

export type ChartPointWithMa = ChartPointOhlc & {
  sma20: number | null
  ema9: number | null
  ema21: number | null
  /** User-chosen EMA period (values filled by `addCustomEmaToChartPoints`). */
  emaCustom: number | null
  /** Trailing swing low from API (`srSupport`), or null (e.g. offline MA path). */
  srSupport: number | null
  /** Trailing swing high from API (`srResistance`), or null (e.g. offline MA path). */
  srResistance: number | null
}

/** Stroke colors aligned across Recharts and SVG candlestick. */
export const MA_LINE_COLORS = {
  sma20: '#fbbf24',
  ema9: '#a78bfa',
  ema21: '#38bdf8',
  emaCustom: '#fb923c',
} as const

export const SR_LINE_COLORS = {
  support: '#34d399',
  resistance: '#f87171',
} as const

/** Toggles for SMA / EMA overlays on line, bar, and candle charts. */
export type MaLineVisibility = {
  showSma20: boolean
  showEma9: boolean
  showEma21: boolean
  showCustomEma: boolean
  showSupportResistance: boolean
}

export const DEFAULT_MA_LINE_VISIBILITY: MaLineVisibility = {
  showSma20: true,
  showEma9: true,
  showEma21: true,
  showCustomEma: false,
  showSupportResistance: true,
}

export const CUSTOM_EMA_PERIOD_MIN = 2
export const CUSTOM_EMA_PERIOD_MAX = 500
export const CUSTOM_EMA_DEFAULT_PERIOD = 50

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

/** Min/max for Recharts Y axis so MA lines stay visible with OHLC (line / bar / candle data). */
export function yDomainForOhlcAndVisibleMas(
  data: ChartPointWithMa[],
  visibility: MaLineVisibility,
): [number, number] | undefined {
  if (data.length === 0) return undefined
  let min = Infinity
  let max = -Infinity
  for (const c of data) {
    min = Math.min(min, c.low, c.high, c.close)
    max = Math.max(max, c.low, c.high, c.close)
    if (visibility.showSma20) {
      const v = c.sma20
      if (v != null && Number.isFinite(v)) {
        min = Math.min(min, v)
        max = Math.max(max, v)
      }
    }
    if (visibility.showEma9) {
      const v = c.ema9
      if (v != null && Number.isFinite(v)) {
        min = Math.min(min, v)
        max = Math.max(max, v)
      }
    }
    if (visibility.showEma21) {
      const v = c.ema21
      if (v != null && Number.isFinite(v)) {
        min = Math.min(min, v)
        max = Math.max(max, v)
      }
    }
    if (visibility.showCustomEma) {
      const v = c.emaCustom
      if (v != null && Number.isFinite(v)) {
        min = Math.min(min, v)
        max = Math.max(max, v)
      }
    }
    if (visibility.showSupportResistance) {
      const s = c.srSupport
      const r = c.srResistance
      if (s != null && Number.isFinite(s)) {
        min = Math.min(min, s)
        max = Math.max(max, s)
      }
      if (r != null && Number.isFinite(r)) {
        min = Math.min(min, r)
        max = Math.max(max, r)
      }
    }
  }
  if (!Number.isFinite(min) || !Number.isFinite(max)) return undefined
  if (min === max) {
    const pad = min === 0 ? 1 : Math.abs(min) * 0.001
    return [min - pad, max + pad]
  }
  const span = max - min
  const pad = span * 0.02
  return [min - pad, max + pad]
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
    emaCustom: null,
    srSupport: null,
    srResistance: null,
  }))
}

/** Add or clear the custom EMA column (closes-only; same seeding as other EMAs). */
export function addCustomEmaToChartPoints(points: ChartPointWithMa[], period: number | null): ChartPointWithMa[] {
  if (points.length === 0) return []
  if (period == null || !Number.isFinite(period)) {
    return points.map((p) => ({ ...p, emaCustom: null }))
  }
  const pInt = Math.floor(period)
  if (pInt < CUSTOM_EMA_PERIOD_MIN || pInt > CUSTOM_EMA_PERIOD_MAX) {
    return points.map((p) => ({ ...p, emaCustom: null }))
  }
  const closes = points.map((x) => x.close)
  const emaC = computeEma(closes, pInt)
  return points.map((pt, i) => ({ ...pt, emaCustom: emaC[i] }))
}
