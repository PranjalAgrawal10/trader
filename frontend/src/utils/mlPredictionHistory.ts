import type { ChartPointWithMa } from './movingAverages'

/** Must match backend `PriceDirectionModelIds.MlNetLightGbmTripleBarrierV1` (LightGBM engine id). */
export const ML_LIGHTGBM_TRIPLE_BARRIER_MODEL_ID = 'mlnet-lightgbm-triple-barrier-v1'

export type MlPredictionOutcome = 'pending' | 'correct' | 'wrong'

export type MlPredictionLogEntry = {
  id: string
  predictedAt: string
  refBarTime: string
  refClose: number
  direction: 'up' | 'down' | 'neutral'
  confidence: number
  modelId: string
  /** Registry engine id when the server stored it (classic + LightGBM). */
  engineModelId?: string | null
  detail: string
  outcome: MlPredictionOutcome
  nextBarTime?: string | null
  nextOpen?: number | null
  nextClose?: number | null
  /** When true, outcome updates are synced via the API */
  serverBacked?: boolean
}

type PriceDirectionLike = {
  direction: 'up' | 'down' | 'neutral'
  confidence: number
  modelId: string
  detail: string
}

/** Cap retained in localStorage (newest kept). Prevents unbounded growth / quota errors. */
export const ML_PREDICTION_HISTORY_MAX_ENTRIES = 20_000

const MAX_ENTRIES = ML_PREDICTION_HISTORY_MAX_ENTRIES

export function mlHistoryStorageKey(instrumentToken: string, interval: string): string {
  return `trader.mlPriceDir.v1:${instrumentToken}:${interval}`
}

export function loadMlHistory(instrumentToken: string, interval: string): MlPredictionLogEntry[] {
  if (typeof window === 'undefined') return []
  try {
    const raw = window.localStorage.getItem(mlHistoryStorageKey(instrumentToken, interval))
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    return parsed.filter(isValidEntry).slice(-MAX_ENTRIES)
  } catch {
    return []
  }
}

function isValidEntry(x: unknown): x is MlPredictionLogEntry {
  if (x == null || typeof x !== 'object') return false
  const o = x as Record<string, unknown>
  return (
    typeof o.id === 'string' &&
    typeof o.predictedAt === 'string' &&
    typeof o.refBarTime === 'string' &&
    typeof o.refClose === 'number' &&
    (o.direction === 'up' || o.direction === 'down' || o.direction === 'neutral') &&
    typeof o.confidence === 'number' &&
    typeof o.modelId === 'string' &&
    typeof o.detail === 'string' &&
    (o.outcome === 'pending' || o.outcome === 'correct' || o.outcome === 'wrong')
  )
}

/** Response row from GET /api/v1/predictions/price-direction/history */
export type MlPriceDirectionHistoryApiRow = {
  id: string
  predictedAt: string
  refBarTime: string
  refClose: number
  direction: 'up' | 'down' | 'neutral'
  confidence: number
  modelId: string
  /** Persisted registry engine id when set (matches automation dedupe row). */
  engineModelId?: string | null
  detail: string
  outcome: MlPredictionOutcome
  nextBarTime: string | null
  nextOpen?: number | null
  nextClose: number | null
  labelThresholdFractionApplied?: number | null
  censorReason?: string | null
  labelNextBar?: number | null
  labelN3?: number | null
  labelN5?: number | null
  nextBarTimeN3?: string | null
  nextCloseN3?: number | null
  nextBarTimeN5?: string | null
  nextCloseN5?: number | null
}

function toIsoBarTime(s: string): string {
  const d = new Date(s)
  return Number.isNaN(d.getTime()) ? s : d.toISOString()
}

/** Table display: most recently predicted rows first (stable for API + localStorage orders). */
export function sortByPredictedAtNewestFirst<T extends { predictedAt: string }>(rows: readonly T[]): T[] {
  return [...rows].sort((a, b) => {
    const tb = new Date(b.predictedAt).getTime()
    const ta = new Date(a.predictedAt).getTime()
    return tb - ta
  })
}

export function historyItemsFromApi(rows: MlPriceDirectionHistoryApiRow[]): MlPredictionLogEntry[] {
  return rows.map((x) => ({
    id: x.id,
    predictedAt: x.predictedAt,
    refBarTime: toIsoBarTime(x.refBarTime),
    refClose: x.refClose,
    direction: x.direction,
    confidence: x.confidence,
    modelId: x.modelId,
    engineModelId: x.engineModelId ?? undefined,
    detail: x.detail,
    outcome: x.outcome,
    nextBarTime: x.nextBarTime ? toIsoBarTime(x.nextBarTime) : null,
    nextOpen: x.nextOpen ?? undefined,
    nextClose: x.nextClose ?? undefined,
    serverBacked: true,
  }))
}

export function saveMlHistory(
  instrumentToken: string,
  interval: string,
  entries: MlPredictionLogEntry[],
): void {
  if (typeof window === 'undefined') return
  try {
    const trimmed = entries.slice(-MAX_ENTRIES)
    window.localStorage.setItem(mlHistoryStorageKey(instrumentToken, interval), JSON.stringify(trimmed))
  } catch {
    // ignore quota / private mode
  }
}

function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `${Date.now()}-${Math.random().toString(36).slice(2, 11)}`
}

function barTimesMatch(chartT: string, entryRefT: string): boolean {
  if (chartT === entryRefT) return true
  const a = new Date(chartT).getTime()
  const b = new Date(entryRefT).getTime()
  return !Number.isNaN(a) && !Number.isNaN(b) && a === b
}

/** Group prediction rows by the candle index whose open time matches <code>refBarTime</code> in the visible series. */
export function groupMlPredictionsByChartBarIndex(
  entries: readonly MlPredictionLogEntry[],
  chartData: readonly { t: string }[],
): Map<number, MlPredictionLogEntry[]> {
  const m = new Map<number, MlPredictionLogEntry[]>()
  for (const e of entries) {
    const i = chartData.findIndex((p) => barTimesMatch(p.t, e.refBarTime))
    if (i < 0) continue
    const cur = m.get(i)
    if (cur) cur.push(e)
    else m.set(i, [e])
  }
  return m
}

export type MlRefBarChartMarker = {
  dataIndex: number
  /** <code>ChartPointWithMa.idx</code> for Recharts X when the same window is used. */
  rechartsX: number
  entries: readonly MlPredictionLogEntry[]
}

/** Markers for every chart bar that has at least one ML prediction (classic + LightGBM rows). */
export function mlRefBarMarkersForVisibleChart(
  entries: readonly MlPredictionLogEntry[],
  chartData: readonly ChartPointWithMa[],
): MlRefBarChartMarker[] {
  const grouped = groupMlPredictionsByChartBarIndex(entries, chartData)
  const out: MlRefBarChartMarker[] = []
  for (const [di, list] of grouped) {
    const pt = chartData[di]
    if (!pt) continue
    out.push({ dataIndex: di, rechartsX: pt.idx, entries: list })
  }
  out.sort((a, b) => a.rechartsX - b.rechartsX)
  return out
}

/**
 * Predictions whose <strong>next bar</strong> is the candle at <paramref name="sliceIndex"/>
 * (resolved <c>nextBarTime</c> match), plus <strong>pending</strong> rows whose ref bar is the previous candle
 * (next bar not written yet).
 */
export function predictionsForTargetBarSlice(
  entries: readonly MlPredictionLogEntry[],
  chartData: readonly { t: string }[],
  sliceIndex: number,
): MlPredictionLogEntry[] {
  const bar = chartData[sliceIndex]
  if (!bar) return []
  const prevBar = sliceIndex > 0 ? chartData[sliceIndex - 1] : null
  const seen = new Set<string>()
  const out: MlPredictionLogEntry[] = []
  for (const e of entries) {
    if (e.nextBarTime != null && barTimesMatch(e.nextBarTime, bar.t)) {
      if (!seen.has(e.id)) {
        seen.add(e.id)
        out.push(e)
      }
      continue
    }
    if (e.outcome === 'pending' && prevBar != null && barTimesMatch(e.refBarTime, prevBar.t)) {
      if (!seen.has(e.id)) {
        seen.add(e.id)
        out.push(e)
      }
    }
  }
  return sortByPredictedAtNewestFirst(out)
}

/** Map chart slice index → predictions that target that bar (see {@link predictionsForTargetBarSlice}). */
export function mapMlPredictionsPerTargetBar(
  entries: readonly MlPredictionLogEntry[],
  chartData: readonly { t: string }[],
): Map<number, MlPredictionLogEntry[]> {
  const m = new Map<number, MlPredictionLogEntry[]>()
  for (let i = 0; i < chartData.length; i++) {
    const preds = predictionsForTargetBarSlice(entries, chartData, i)
    if (preds.length > 0) m.set(i, preds)
  }
  return m
}

/** On-chart label: only next-bar directions, e.g. <c>(up, down, neutral)</c>. Stable order by <c>modelId</c>. */
export function formatMlTargetBarRibbon(entries: readonly MlPredictionLogEntry[]): string | null {
  if (entries.length === 0) return null
  const sorted = [...entries].sort((a, b) => {
    const c = a.modelId.localeCompare(b.modelId, undefined, { sensitivity: 'base' })
    if (c !== 0) return c
    return a.predictedAt.localeCompare(b.predictedAt)
  })
  return `(${sorted.map((e) => e.direction).join(', ')})`
}

/** Compare predicted next-bar direction vs following candle close vs ref close. */
export function resolveMlEntry(entry: MlPredictionLogEntry, series: ChartPointWithMa[]): MlPredictionLogEntry {
  if (entry.outcome !== 'pending') return entry
  const i = series.findIndex((p) => barTimesMatch(p.t, entry.refBarTime))
  if (i < 0 || i + 1 >= series.length) return entry
  const next = series[i + 1]
  const nextClose = next.close
  const nextBarTime = next.t
  const actual: 'up' | 'down' | 'neutral' =
    nextClose > entry.refClose ? 'up' : nextClose < entry.refClose ? 'down' : 'neutral'

  let outcome: MlPredictionOutcome
  if (entry.direction === 'neutral') outcome = actual === 'neutral' ? 'correct' : 'wrong'
  else if (entry.direction === 'up')
    outcome = actual === 'up' ? 'correct' : actual === 'down' ? 'wrong' : 'wrong'
  else outcome = actual === 'down' ? 'correct' : actual === 'up' ? 'wrong' : 'wrong'

  return {
    ...entry,
    outcome,
    nextBarTime,
    nextClose,
  }
}

export function resolveMlHistory(entries: MlPredictionLogEntry[], series: ChartPointWithMa[]): MlPredictionLogEntry[] {
  return entries.map((e) => resolveMlEntry(e, series))
}

export function appendMlPrediction(
  entries: MlPredictionLogEntry[],
  pred: PriceDirectionLike,
  ref: { t: string; close: number },
  opts?: { serverId?: string; predictedAt?: string },
): MlPredictionLogEntry[] {
  const next: MlPredictionLogEntry = {
    id: opts?.serverId ?? newId(),
    predictedAt: opts?.predictedAt ?? new Date().toISOString(),
    refBarTime: ref.t,
    refClose: ref.close,
    direction: pred.direction,
    confidence: pred.confidence,
    modelId: pred.modelId,
    detail: pred.detail,
    outcome: 'pending',
    serverBacked: opts?.serverId != null,
  }
  return [...entries, next].slice(-MAX_ENTRIES)
}

export function historiesEqual(a: MlPredictionLogEntry[], b: MlPredictionLogEntry[]): boolean {
  if (a.length !== b.length) return false
  for (let i = 0; i < a.length; i++) {
    if (JSON.stringify(a[i]) !== JSON.stringify(b[i])) return false
  }
  return true
}
