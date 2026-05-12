import { candleBucketStartUtc, type ChartIntervalKey } from './liveCandleMerge'
import type { ChartPointOhlc } from './liveCandleMerge'

/** Payload from `/broker/kite/demo-paper-positions` (camelCase serialized). */
export type DemoPaperOpenBuyMarkerLite = {
  boughtAtUtc: string
  contractsRemaining?: number
}

/**
 * Maps each OPEN demo paper buy to a 0-based index into visible `chartData` (already zoom-sliced; `idx` is renumbered).
 * Buys attributed to bar open buckets via {@link candleBucketStartUtc} (same semantics as merging live ticks).
 */
export function chartDataIndicesForPaperBuyMarkers(
  markers: readonly DemoPaperOpenBuyMarkerLite[],
  chartData: readonly ChartPointOhlc[],
  intervalKey: ChartIntervalKey,
): readonly number[] {
  if (chartData.length === 0 || markers.length === 0) return []
  const indices: number[] = []
  outer: for (const m of markers) {
    const tradeMs = Date.parse(m.boughtAtUtc)
    if (!Number.isFinite(tradeMs)) continue outer
    const bucket = candleBucketStartUtc(tradeMs, intervalKey)
    for (let i = 0; i < chartData.length; i++) {
      const rowMs = Date.parse(chartData[i].t)
      if (!Number.isFinite(rowMs)) continue
      if (candleBucketStartUtc(rowMs, intervalKey) === bucket) {
        indices.push(i)
        continue outer
      }
    }
  }
  return indices
}
