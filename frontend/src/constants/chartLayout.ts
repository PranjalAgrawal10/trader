/**
 * Fraction of chart container width kept empty on the right (readable margin / LTP labels).
 * Used by {@link CandlestickChart} geometry and {@link ChartWithRightGutter} for Recharts.
 */
export const CHART_RIGHT_EDGE_GAP_FRACT = 0.05

/** Max horizontal spacing per OHLC candle when clustering (zoomed views); drives layout + drag-to-pan scale. */
export const CHART_CANDLE_MAX_SLOT_PX = 28
