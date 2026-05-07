import type { ChartPointWithMa } from './movingAverages'

export type MlPredictionOutcome = 'pending' | 'correct' | 'wrong'

export type MlPredictionLogEntry = {
  id: string
  predictedAt: string
  refBarTime: string
  refClose: number
  direction: 'up' | 'down' | 'neutral'
  confidence: number
  modelId: string
  detail: string
  outcome: MlPredictionOutcome
  nextBarTime?: string | null
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
  detail: string
  outcome: MlPredictionOutcome
  nextBarTime: string | null
  nextClose: number | null
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
    detail: x.detail,
    outcome: x.outcome,
    nextBarTime: x.nextBarTime ? toIsoBarTime(x.nextBarTime) : null,
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
