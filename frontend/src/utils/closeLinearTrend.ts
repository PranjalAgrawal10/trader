import type { ChartPointWithMa, MaLineVisibility } from './movingAverages'
import { yDomainForOhlcAndVisibleMas } from './movingAverages'

/** Linear regression overlay on closes (distinct from SMA/EMA). */
export const LINEAR_CLOSE_TREND_COLOR = '#d946ef'

export type ChartPointWithMaAndTrend = ChartPointWithMa & { trendLine: number | null }

/** Least-squares fit of <code>y[i] ~ a + b·i</code> over visible bars (index 0 … n−1). */
export function linearRegressionCloseTrend(values: readonly number[]): (number | null)[] {
  const n = values.length
  if (n < 2) return Array.from({ length: n }, () => null)

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
  if (Math.abs(denom) < 1e-12) return Array.from({ length: n }, () => null)

  const slope = (n * sumIY - sumI * sumY) / denom
  const intercept = (sumY - slope * sumI) / n
  return values.map((_, i) => intercept + slope * i)
}

export function attachLinearTrendToChartPoints(points: ChartPointWithMa[]): ChartPointWithMaAndTrend[] {
  if (points.length === 0) return []

  const closes = points.map((p) => p.close)
  const trend = linearRegressionCloseTrend(closes)
  return points.map((p, i) => ({ ...p, trendLine: trend[i] }))
}

/** Y-domain for Recharts including each bar's `trendLine` value (same padding rule as OHLC+MAs). */
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
