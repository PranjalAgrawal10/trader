import type { ChartPointOhlc } from './liveCandleMerge'

/** Minimum visible bars when zoomed in (latest candles on the right). MA lines need history; at 1 bar they are usually empty. */
export const CHART_ZOOM_MIN_BARS = 1

const ZOOM_IN_RATIO = 0.55
const ZOOM_OUT_RATIO = 1 / ZOOM_IN_RATIO

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

export function zoomInBarCount(current: number | null, total: number): number {
  if (total <= CHART_ZOOM_MIN_BARS) return total
  const cur = current ?? total
  const next = Math.max(CHART_ZOOM_MIN_BARS, Math.floor(cur * ZOOM_IN_RATIO))
  return next < cur ? next : Math.max(CHART_ZOOM_MIN_BARS, cur - 1)
}

export function zoomOutBarCount(current: number | null, total: number): number | null {
  if (current == null || total <= CHART_ZOOM_MIN_BARS) return null
  const next = Math.min(total, Math.ceil(current * ZOOM_OUT_RATIO))
  return next >= total ? null : next
}
