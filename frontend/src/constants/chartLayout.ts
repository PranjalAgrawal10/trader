/**
 * Fraction of chart container width kept empty on the right (readable margin / LTP labels).
 * Used by OHLC chart layout and {@link ChartWithRightGutter} gutter alignment.
 */
export const CHART_RIGHT_EDGE_GAP_FRACT = 0.05

/** Max horizontal spacing per OHLC candle when clustering (zoomed views). */
export const CHART_CANDLE_MAX_SLOT_PX = 28

/**
 * TradingView LW `timeScale.minBarSpacing`: lower bound on bar spacing (zooming out cannot pack candles tighter than this).
 * Keeps bodies/gaps readable; must be less than {@link CHART_LW_MAX_BAR_SPACING_PX}.
 */
export const CHART_LW_MIN_BAR_SPACING_PX = 3

/**
 * TradingView LW `timeScale.maxBarSpacing`: upper bound on bar spacing (zooming in cannot exceed this).
 * Avoids a few giant candles; use `0`-style “unlimited” only if you omit this from chart options.
 */
export const CHART_LW_MAX_BAR_SPACING_PX = 48

/** Default number of newest candles shown initially (LW pan reveals the rest locally; pan left triggers older fetch). */
export const CHART_DEFAULT_VISIBLE_BARS = 24

/** When visible logical bar index touches this threshold, request older candles (unless exhausted). */
export const CHART_LOAD_OLDER_VISIBLE_THRESHOLD = 8

/** Minimum span for an “older chunk” backward request (avoid zero/narrow gaps). */
export const CHART_OLDER_CHUNK_MIN_MS = 60_000

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
