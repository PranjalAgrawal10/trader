import type { HistogramData } from 'lightweight-charts'
import type {
  CandlestickData,
  BarData,
  LineData,
  UTCTimestamp,
} from 'lightweight-charts'
import type { ChartPointWithMa, MaLineVisibility } from './movingAverages'
import type { ChartPointWithMaAndTrend } from './closeLinearTrend'

export function utcTimestampFromIsoSec(iso: string): UTCTimestamp {
  return Math.floor(Date.parse(iso) / 1000) as UTCTimestamp
}

/** Unique, monotonic LW times aligned to sliced bar order (fixes duplicate-second bars). */
export function barTimesUtc(data: ChartPointWithMa[], ghostBars: number): UTCTimestamp[] {
  const out: UTCTimestamp[] = []
  let last = Number.NEGATIVE_INFINITY
  for (const pt of data) {
    let t = utcTimestampFromIsoSec(pt.t) as unknown as number
    if (!Number.isFinite(t)) t = last <= 0 ? 1 : last + 1
    if (t <= last) t = last + 1
    last = t
    out.push(t as UTCTimestamp)
  }
  if (ghostBars <= 0 || data.length === 0) return out

  const step = inferBarSpacingSec(out, data.length)
  for (let g = 1; g <= ghostBars; g++) {
    const tNum = last + g * step
    last = tNum
    out.push(last as UTCTimestamp)
  }
  return out
}

function inferBarSpacingSec(times: UTCTimestamp[], barCount: number): number {
  if (barCount >= 2) {
    const a = times[barCount - 2] as unknown as number
    const b = times[barCount - 1] as unknown as number
    return Math.max(1, Math.floor(b - a))
  }
  return 60
}

export function lwCandlesFromBars(
  data: ChartPointWithMa[],
  times: UTCTimestamp[],
): CandlestickData<UTCTimestamp>[] {
  const n = data.length
  const ghost = times.length - n
  const out: CandlestickData<UTCTimestamp>[] = []
  for (let i = 0; i < n; i++) {
    const c = data[i]
    out.push({
      time: times[i],
      open: c.open,
      high: c.high,
      low: c.low,
      close: c.close,
    })
  }
  const lastClose = n > 0 ? data[n - 1].close : 0
  for (let g = 0; g < ghost; g++) {
    const ti = times[n + g]
    out.push({
      time: ti,
      open: lastClose,
      high: lastClose,
      low: lastClose,
      close: lastClose,
    })
  }
  return out
}

export function lwBarsFromBars(data: ChartPointWithMa[], times: UTCTimestamp[]): BarData<UTCTimestamp>[] {
  return lwCandlesFromBars(data, times).map(({ time, open, high, low, close }) => ({
    time,
    open,
    high,
    low,
    close,
  }))
}

export function lwVolumesFromBars(
  data: ChartPointWithMa[],
  times: UTCTimestamp[],
): HistogramData<UTCTimestamp>[] {
  const n = data.length
  const ghost = times.length - n
  const out: HistogramData<UTCTimestamp>[] = []
  for (let i = 0; i < n; i++) {
    const c = data[i]
    const v = Number(c.volume)
    const bullish = c.close >= c.open
    const color =
      bullish && v > 0 ? 'rgba(34,197,94,0.62)' : v > 0 ? 'rgba(239,68,68,0.62)' : 'rgba(173,181,189,0.06)'
    out.push({ time: times[i], value: Number.isFinite(v) ? Math.max(v, 0) : 0, color })
  }
  for (let g = 0; g < ghost; g++) {
    out.push({ time: times[n + g], value: 0, color: 'rgba(173,181,189,0.04)' })
  }
  return out
}

export function lwSingleValueSkipNull<T extends { t: string }>(
  pts: readonly T[],
  times: UTCTimestamp[],
  valueKey: keyof T & string,
): LineData<UTCTimestamp>[] {
  const out: LineData<UTCTimestamp>[] = []
  for (let i = 0; i < pts.length; i++) {
    const raw = pts[i][valueKey] as unknown
    if (raw == null || !Number.isFinite(Number(raw))) continue
    out.push({ time: times[i], value: Number(raw) })
  }
  return out
}

export function lwCloseLine(data: ChartPointWithMa[], times: UTCTimestamp[]): LineData<UTCTimestamp>[] {
  return data.map((c, i) => ({ time: times[i], value: c.close })) as LineData<UTCTimestamp>[]
}

export function lwTrendLine(
  trendPts: ChartPointWithMaAndTrend[] | null,
  times: UTCTimestamp[],
): LineData<UTCTimestamp>[] {
  if (!trendPts || trendPts.length === 0) return []
  const out: LineData<UTCTimestamp>[] = []
  for (let i = 0; i < trendPts.length; i++) {
    const v = trendPts[i].trendLine
    if (v == null || !Number.isFinite(v)) continue
    out.push({ time: times[i], value: v })
  }
  return out
}

export function lwMaSeriesKeys(vis: MaLineVisibility, customPeriod: number | null): Partial<
  Record<'sma20' | 'ema9' | 'ema21' | 'emaCustom' | 'srSupport' | 'srResistance', true>
> {
  const keys: Partial<Record<'sma20' | 'ema9' | 'ema21' | 'emaCustom' | 'srSupport' | 'srResistance', true>> = {}
  if (vis.showSma20) keys.sma20 = true
  if (vis.showEma9) keys.ema9 = true
  if (vis.showEma21) keys.ema21 = true
  if (vis.showCustomEma && customPeriod != null && customPeriod >= 2) keys.emaCustom = true
  if (vis.showSupportResistance) keys.srSupport = true
  if (vis.showSupportResistance) keys.srResistance = true
  return keys
}
