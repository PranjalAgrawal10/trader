import type { ChartPointOhlc } from './liveCandleMerge'

/** Minimum visible bars when zoomed in (latest candles on the right). MA lines need history; at 1 bar they are usually empty. */
export const CHART_ZOOM_MIN_BARS = 1

const ZOOM_IN_RATIO = 0.55
const ZOOM_OUT_RATIO = 1 / ZOOM_IN_RATIO

/** Per-instrument persisted zoom: fractional window on the loaded series (exclusive 0–1), or a legacy saved bar count (≥ 1 integer). */
export type ChartZoomStored = number | null

/**
 * Resolves persisted zoom to a slice width in bars after a history refresh.
 * - Values in (0, 1): fraction of `totalBars` (`round`).
 * - Values ≥ 1: legacy persisted bar counts from older clients.
 */
export function visibleBarsFromChartZoomStored(
  stored: number | null | undefined,
  totalBars: number,
): number | null {
  if (stored == null || !Number.isFinite(stored) || stored <= 0 || totalBars <= 0) return null

  let vb: number
  if (stored < 1) {
    vb = Math.max(CHART_ZOOM_MIN_BARS, Math.min(totalBars, Math.round(totalBars * stored)))
  } else {
    vb = Math.max(CHART_ZOOM_MIN_BARS, Math.min(totalBars, Math.floor(stored + 1e-9)))
  }

  return vb >= totalBars ? null : vb
}

/** Rounds fractional zoom for PUT bodies / dict keys (matches server rounding). */
export function roundChartZoomFraction(fraction: number): number {
  return Number.parseFloat((Math.round(fraction * 1e6) / 1e6).toFixed(6))
}

/**
 * When `stored` is obsolete (full range implied) migrate/clear zoom, or migrate legacy integer bar counts to fractions.
 * Returns undefined when no correction is needed.
 */
export function correctedChartZoomStored(
  stored: number | null | undefined,
  totalBars: number,
): ChartZoomStored | undefined {
  if (stored == null || !Number.isFinite(stored) || stored <= 0 || totalBars <= 0) return undefined

  const vb = visibleBarsFromChartZoomStored(stored, totalBars)
  if (vb == null) return null

  if (stored >= 1) return roundChartZoomFraction(vb / totalBars)
  return undefined
}

/** Narrow the visible fraction of the downloaded series (~same step feel as legacy bar-ratio zoom). */
export function zoomInChartZoomStored(stored: ChartZoomStored, totalBars: number): ChartZoomStored {
  if (totalBars <= CHART_ZOOM_MIN_BARS) return null
  const curBars = visibleBarsFromChartZoomStored(stored ?? null, totalBars) ?? totalBars
  let nextBars = Math.max(CHART_ZOOM_MIN_BARS, Math.floor(curBars * ZOOM_IN_RATIO))
  if (nextBars >= curBars && curBars > CHART_ZOOM_MIN_BARS) nextBars = curBars - 1
  if (nextBars >= totalBars) return null
  return roundChartZoomFraction(nextBars / totalBars)
}

/** Widen toward the full downloaded series */
export function zoomOutChartZoomStored(stored: ChartZoomStored, totalBars: number): ChartZoomStored {
  if (totalBars <= CHART_ZOOM_MIN_BARS) return null
  const curBars = visibleBarsFromChartZoomStored(stored ?? null, totalBars)
  if (curBars == null) return null
  const nextBars = Math.min(totalBars, Math.ceil(curBars * ZOOM_OUT_RATIO))
  if (nextBars >= totalBars) return null
  return roundChartZoomFraction(nextBars / totalBars)
}

/** How far the zoom window can slide left (into older bars). {@link sliceChartForZoom} clamps {@link panOffsetBars} to this range. */
export function maxChartPanOffsetBars(totalBars: number, visibleBarCount: number | null): number {
  if (totalBars <= 0 || visibleBarCount == null || totalBars <= visibleBarCount) return 0
  return totalBars - visibleBarCount
}

export function clampChartPanOffsetBars(
  panOffsetBars: number,
  totalBars: number,
  visibleBarCount: number | null,
): number {
  const m = maxChartPanOffsetBars(totalBars, visibleBarCount)
  return Math.min(Math.max(0, panOffsetBars), m)
}

/** Full zoom pan clamp including negative “newer” ghost pull (≤ −visible count when zoomed). */
export function clampChartPanAllowNewerGhost(
  panOffsetBars: number,
  totalBars: number,
  visibleBarCount: number | null,
): number {
  const maxP = maxChartPanOffsetBars(totalBars, visibleBarCount)
  if (visibleBarCount == null || totalBars <= visibleBarCount) {
    if (panOffsetBars < 0) return 0
    return panOffsetBars > maxP ? maxP : panOffsetBars
  }
  const minP = -visibleBarCount
  if (panOffsetBars < minP) return minP
  if (panOffsetBars > maxP) return maxP
  return panOffsetBars
}

/** Newest bar centered on line/bar; {@link newerGhostBars} lengthens axis into empty “future” space. */
export function xAxisDomainCenterLatest(
  data: readonly { idx: number }[],
  newerGhostBars = 0,
): [number, number] | undefined {
  if (data.length === 0) return undefined
  const idxMin = data[0].idx
  const idxMax = data[data.length - 1].idx
  if (idxMin > idxMax) return undefined
  const g = Math.max(0, newerGhostBars)
  if (idxMin === idxMax) return [idxMin - 0.5 - g, idxMax + 0.5 + g]
  return [idxMin, 2 * idxMax - idxMin + 2 * g]
}

/**
 * When zoomed, shows a sliding window of {@link visibleBarCount} bars. {@link panOffsetBars} moves the window left
 * (older): 0 = newest bar in data; negative values are ignored for slicing (ghost pull is renderer-only via chart props).
 */
export function sliceChartForZoom<T extends ChartPointOhlc>(
  points: T[],
  visibleBarCount: number | null,
  panOffsetBars = 0,
): T[] {
  if (points.length === 0) return points
  const total = points.length
  if (visibleBarCount == null || total <= visibleBarCount) {
    return points.map((p, i) => ({ ...p, idx: i + 1 }))
  }
  const pan = clampChartPanOffsetBars(Math.max(0, panOffsetBars), total, visibleBarCount)
  const end = total - pan
  const start = end - visibleBarCount
  const slice = points.slice(Math.max(0, start), end)
  return slice.map((p, i) => ({ ...p, idx: i + 1 }))
}
