/** Shared Kite chart toolbar types + historical range query (keep in sync with server defaults). */

/** Per-chart historical OHLC refresh while mounted (Instruments browse / favorites / manual trade). */
export const CHART_LIVE_POLL_MS = 60_000

export const CHART_INTERVALS = [
  '1m',
  '2m',
  '3m',
  '4m',
  '5m',
  '10m',
  '15m',
  '30m',
  '1h',
  '4h',
  '1d',
  '1w',
] as const
export type ChartInterval = (typeof CHART_INTERVALS)[number]

/** Lookback for historical request. `auto` = omit from/to (server default per interval). */
export const CHART_RANGE_PRESETS = [
  'auto',
  'last5m',
  'last10m',
  'last15m',
  'last30m',
  'last1h',
  'last5h',
  'last10h',
  'last1d',
  'last2d',
  'last3d',
  'last5d',
  'last1mo',
] as const
export type ChartRangePreset = (typeof CHART_RANGE_PRESETS)[number]

export const CHART_RANGE_LABEL: Record<ChartRangePreset, string> = {
  auto: 'Auto',
  last5m: '5m',
  last10m: '10m',
  last15m: '15m',
  last30m: '30m',
  last1h: '1h',
  last5h: '5h',
  last10h: '10h',
  last1d: '1d',
  last2d: '2d',
  last3d: '3d',
  last5d: '5d',
  last1mo: '1mo',
}

export type ChartGraphType = 'line' | 'bar' | 'candlestick'

export function historicalRangeQueryParams(preset: ChartRangePreset): { from?: string; to?: string } {
  if (preset === 'auto') return {}
  const to = new Date()
  const from = new Date(to.getTime())
  switch (preset) {
    case 'last5m':
      from.setUTCMinutes(from.getUTCMinutes() - 5)
      break
    case 'last10m':
      from.setUTCMinutes(from.getUTCMinutes() - 10)
      break
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
    case 'last10h':
      from.setUTCHours(from.getUTCHours() - 10)
      break
    case 'last1d':
      from.setUTCDate(from.getUTCDate() - 1)
      break
    case 'last2d':
      from.setUTCDate(from.getUTCDate() - 2)
      break
    case 'last3d':
      from.setUTCDate(from.getUTCDate() - 3)
      break
    case 'last5d':
      from.setUTCDate(from.getUTCDate() - 5)
      break
    case 'last1mo':
      from.setUTCMonth(from.getUTCMonth() - 1)
      break
  }
  return { from: from.toISOString(), to: to.toISOString() }
}

export function coerceChartInterval(v: string | null | undefined): ChartInterval {
  if (v && (CHART_INTERVALS as readonly string[]).includes(v)) return v as ChartInterval
  return '5m'
}

export function coerceChartRangePreset(v: string | null | undefined): ChartRangePreset {
  if (v && (CHART_RANGE_PRESETS as readonly string[]).includes(v)) return v as ChartRangePreset
  return 'auto'
}

export function coerceChartGraphType(v: string | null | undefined): ChartGraphType {
  if (v === 'bar' || v === 'line' || v === 'candlestick') return v
  if (v === 'trend') return 'candlestick'
  return 'line'
}
