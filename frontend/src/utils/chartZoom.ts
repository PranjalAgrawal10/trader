import type { ChartPointOhlc } from './liveCandleMerge'

/**
 * When zoomed, shows a sliding window of {@link visibleBarCount} bars.
 * {@link panOffsetBars} moves the window toward older bars: {@code 0} = newest bar in data.
 *
 * Today all SPA call sites pass {@code visibleBarCount: null} (full downloaded series).
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
  const maxPan = Math.max(0, total - visibleBarCount)
  const pan = Math.min(Math.max(0, panOffsetBars), maxPan)
  const end = total - pan
  const start = end - visibleBarCount
  const slice = points.slice(Math.max(0, start), end)
  return slice.map((p, i) => ({ ...p, idx: i + 1 }))
}
