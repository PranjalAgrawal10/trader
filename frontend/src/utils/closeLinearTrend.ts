import type { ChartPointWithMa, MaLineVisibility } from './movingAverages'
import { yDomainForOhlcAndVisibleMas } from './movingAverages'

/** Linear regression overlay on closes (distinct from SMA/EMA). */
export const LINEAR_CLOSE_TREND_COLOR = '#d946ef'

export type ChartPointWithMaAndTrend = ChartPointWithMa & { trendLine: number | null }

/** Coefficients for least-squares fit <code>y[i] ~ intercept + slope·i</code> over indices 0 … n−1. */
export function linearRegressionCloseCoefficients(
  values: readonly number[],
): { slope: number; intercept: number; n: number } | null {
  const n = values.length
  if (n < 2) return null

  let sumI = 0
  let sumY = 0
  let sumIY = 0
  let sumI2 = 0
  for (let i = 0; i < n; i++) {
    const y = values[i]
    sumI += i
    sumY += y
    sumIY += i * y
    sumI2 += i * i
  }

  const denom = n * sumI2 - sumI * sumI
  if (Math.abs(denom) < 1e-12) return null

  const slope = (n * sumIY - sumI * sumY) / denom
  const intercept = (sumY - slope * sumI) / n
  return { slope, intercept, n }
}

/** Summary of linear regression on closes for multi-timeframe trend readouts. */
export type CloseLinearTrendSummary = {
  barCount: number
  /** dy/di in price units per bar index */
  slopePerBar: number
  /** Fitted value at first / last bar (same as Trend LR line endpoints). */
  fitAtFirst: number
  fitAtLast: number
  firstClose: number
  lastClose: number
  /** (lastClose - firstClose) / firstClose · 100 */
  windowDeltaPct: number
}

export function summarizeCloseLinearTrend(closes: readonly number[]): CloseLinearTrendSummary | null {
  if (closes.length < 2) return null
  const c = linearRegressionCloseCoefficients(closes)
  if (!c) return null
  const { slope, intercept, n } = c
  const lastI = n - 1
  const fitAtFirst = intercept
  const fitAtLast = intercept + slope * lastI
  const firstClose = closes[0]
  const lastClose = closes[closes.length - 1]
  if (!Number.isFinite(firstClose) || firstClose === 0) return null
  return {
    barCount: n,
    slopePerBar: slope,
    fitAtFirst,
    fitAtLast,
    firstClose,
    lastClose,
    windowDeltaPct: ((lastClose - firstClose) / firstClose) * 100,
  }
}

/** Least-squares fit of <code>y[i] ~ a + b·i</code> over visible bars (index 0 … n−1). */
export function linearRegressionCloseTrend(values: readonly number[]): (number | null)[] {
  const c = linearRegressionCloseCoefficients(values)
  if (!c) return Array.from({ length: values.length }, () => null)
  const { slope, intercept } = c
  return values.map((_, i) => intercept + slope * i)
}

export function attachLinearTrendToChartPoints(points: ChartPointWithMa[]): ChartPointWithMaAndTrend[] {
  if (points.length === 0) return []

  const closes = points.map((p) => p.close)
  const trend = linearRegressionCloseTrend(closes)
  return points.map((p, i) => ({ ...p, trendLine: trend[i] }))
}

/** Y-domain including each bar's `trendLine` value (same padding rule as OHLC+MAs); fed into fixed price-scale range. */
export function yDomainForTrendRecharts(
  data: readonly ChartPointWithMaAndTrend[],
  visibility: MaLineVisibility,
): [number, number] | undefined {
  if (data.length === 0) return undefined

  const stripped: ChartPointWithMa[] = data.map(({ trendLine: _ignored, ...r }) => r)
  const base = yDomainForOhlcAndVisibleMas(stripped, visibility)
  if (!base) return undefined

  let min = base[0]
  let max = base[1]
  for (const row of data) {
    const t = row.trendLine
    if (t != null && Number.isFinite(t)) {
      min = Math.min(min, t)
      max = Math.max(max, t)
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
