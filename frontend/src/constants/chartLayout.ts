/**
 * Fraction of chart container width kept empty on the right (readable margin / LTP labels).
 * Used by {@link CandlestickChart} geometry and {@link ChartWithRightGutter} for Recharts.
 */
export const CHART_RIGHT_EDGE_GAP_FRACT = 0.05

/** Max horizontal spacing per OHLC candle when clustering (zoomed views); drives layout + drag-to-pan scale. */
export const CHART_CANDLE_MAX_SLOT_PX = 28

/** Top-left scroll stack when a chart uses browser fullscreen (toolbars, captions, zoom). */
export const CHART_FULLSCREEN_META_WRAP_CLASS =
  'align-self-start text-start mb-2 flex-shrink-0 small border-bottom border-secondary pb-2'

export const CHART_FULLSCREEN_META_WRAP_STYLE: {
  maxHeight: string
  overflowY: 'auto'
  maxWidth: string
  WebkitOverflowScrolling: 'touch'
} = {
  maxHeight: 'min(42vh, 28rem)',
  overflowY: 'auto',
  maxWidth: 'min(42rem, 100%)',
  WebkitOverflowScrolling: 'touch',
}
