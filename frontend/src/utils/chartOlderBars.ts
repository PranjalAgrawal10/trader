import type { ChartPointWithMa } from './movingAverages'
import { CHART_OLDER_CHUNK_MIN_MS } from '../constants/chartLayout'

function barTimeMs(t: string): number {
  const n = Date.parse(t)
  return Number.isFinite(n) ? n : 0
}

function barTimeKey(t: string): string {
  const n = Date.parse(t)
  return Number.isFinite(n) ? String(n) : t
}

/** Dedupe by bar open time; prepend older rows before existing (both ascending by time). */
export function prependOlderChartPoints(
  existing: ChartPointWithMa[],
  older: ChartPointWithMa[],
): ChartPointWithMa[] {
  if (older.length === 0) return existing
  const have = new Set(existing.map((p) => barTimeKey(p.t)))
  const add = older.filter((p) => !have.has(barTimeKey(p.t)))
  if (add.length === 0) return existing
  add.sort((a, b) => barTimeMs(a.t) - barTimeMs(b.t))
  return [...add, ...existing]
}

/** Earliest bar still strictly after the original window start → more history may exist. */
export function canFetchOlderThanWindow(
  earliestBarIso: string,
  windowFromIso: string,
  toleranceMs = 60_000,
): boolean {
  const e = barTimeMs(earliestBarIso)
  const w = barTimeMs(windowFromIso)
  if (e <= 0 || w <= 0) return false
  return e - toleranceMs > w
}

/** Request the previous chunk before <code>earliestBarIso</code>, same span as the loaded window (floored at window <code>from</code>). */
export function buildOlderWindowQuery(
  earliestBarIso: string,
  windowMeta: { from: string; to: string },
  minSpanMs: number = CHART_OLDER_CHUNK_MIN_MS,
): { from: string; to: string } | null {
  const winFrom = barTimeMs(windowMeta.from)
  const winTo = barTimeMs(windowMeta.to)
  const earliest = barTimeMs(earliestBarIso)
  if (winFrom <= 0 || winTo <= 0 || earliest <= 0) return null
  const span = Math.max(minSpanMs, winTo - winFrom)
  const newTo = earliest - 1
  const newFrom = Math.max(winFrom, newTo - span)
  if (!(newFrom < newTo)) return null
  return { from: new Date(newFrom).toISOString(), to: new Date(newTo).toISOString() }
}
