import type { ChartPointOhlc } from './liveCandleMerge'

/** Fewer bars than this are not very useful with MA overlays. */
export const CHART_ZOOM_MIN_BARS = 24

const ZOOM_IN_RATIO = 0.55
const ZOOM_OUT_RATIO = 1 / ZOOM_IN_RATIO

export function sliceChartForZoom<T extends ChartPointOhlc>(points: T[], visibleBarCount: number | null): T[] {
  if (points.length === 0) return points
  const slice =
    visibleBarCount == null || points.length <= visibleBarCount
      ? points
      : points.slice(-visibleBarCount)
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
