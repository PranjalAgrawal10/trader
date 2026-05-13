/** Smallest allowed vertical-zoom scale (< 1 narrows visible price span around the midpoint). */
export const PRICE_VERTICAL_ZOOM_MIN_SCALE = 0.02

/** Each Y zoom step multiplies/divides linear distance from range midpoint (~match horizontal zoom rhythm). */
const PRICE_VERTICAL_ZOOM_STEP_RATIO = 0.82

/**
 * Applies vertical price zoom relative to chart auto-domain: scale 1 preserves domain; smaller scale shows a
 * centered band proportional to span × scale (candle bodies/wicks outside are clipped visually).
 */
export function applyVerticalPriceZoomToDomain(
  domain: [number, number] | undefined,
  scale: number,
): [number, number] | undefined {
  if (domain == null || !Number.isFinite(scale)) return domain
  if (!(scale <= 1 + 1e-9 && scale >= PRICE_VERTICAL_ZOOM_MIN_SCALE)) return domain
  const lo = domain[0]
  const hi = domain[1]
  if (!Number.isFinite(lo) || !Number.isFinite(hi)) return domain
  const span = hi - lo
  if (!(span > 0)) return domain

  const s = Math.min(1, Math.max(PRICE_VERTICAL_ZOOM_MIN_SCALE, scale))
  if (s >= 1 - 1e-12) return [lo, hi]

  const mid = (lo + hi) / 2
  const half = (span / 2) * s
  return [mid - half, mid + half]
}

/** Narrow vertical window (amplify price detail). */
export function zoomInVerticalPriceScale(scale: number): number {
  return Math.max(PRICE_VERTICAL_ZOOM_MIN_SCALE, scale * PRICE_VERTICAL_ZOOM_STEP_RATIO)
}

/** Widen vertical window toward auto scale; cannot exceed 1. */
export function zoomOutVerticalPriceScale(scale: number): number {
  if (!(scale > 0) || !Number.isFinite(scale)) return 1
  const next = scale / PRICE_VERTICAL_ZOOM_STEP_RATIO
  return next >= 1 - 1e-9 ? 1 : Math.min(1, next)
}
