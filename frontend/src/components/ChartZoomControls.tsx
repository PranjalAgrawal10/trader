import { Button, ButtonGroup, Spinner } from 'react-bootstrap'
import { CHART_ZOOM_MIN_BARS } from '../utils/chartZoom'
import { PRICE_VERTICAL_ZOOM_MIN_SCALE } from '../utils/chartVerticalZoom'

/** Tol for treating verticalZoomScale ≈ 1 as “no Y zoom”. */
const PRICE_VERTICAL_ZOOM_EPS = 1e-4

/** Separate X-axis (time / bar window) and Y-axis (price scale) zoom; refresh + fullscreen row. */
export function ChartZoomControls({
  idPrefix,
  totalBars,
  visibleBarCount,
  onHorizontalZoomIn,
  onHorizontalZoomOut,
  onHorizontalZoomReset,
  verticalZoomScale,
  onVerticalZoomIn,
  onVerticalZoomOut,
  onVerticalZoomReset,
  compact,
  onToggleFullscreen,
  fullscreenActive,
  onRefreshChart,
  chartRefreshing,
}: {
  idPrefix: string
  totalBars: number
  visibleBarCount: number | null
  onHorizontalZoomIn: () => void
  onHorizontalZoomOut: () => void
  onHorizontalZoomReset: () => void
  /** 1 = full auto price range; smaller = zoomed vertically. */
  verticalZoomScale: number
  onVerticalZoomIn: () => void
  onVerticalZoomOut: () => void
  onVerticalZoomReset: () => void
  compact?: boolean
  onToggleFullscreen?: () => void
  fullscreenActive?: boolean
  onRefreshChart?: () => void
  chartRefreshing?: boolean
}) {
  const refreshBtn = onRefreshChart ? (
    <Button
      type="button"
      variant="outline-secondary"
      size="sm"
      id={`${idPrefix}-chart-refresh`}
      className={compact ? 'py-0 px-2' : undefined}
      disabled={chartRefreshing}
      onClick={onRefreshChart}
      title="Reload candles from server"
      aria-label="Refresh chart data"
    >
      {chartRefreshing ? (
        <>
          <Spinner animation="border" size="sm" className="me-1" role="status" />
          {compact ? '' : 'Refreshing'}
        </>
      ) : compact ? (
        '↻'
      ) : (
        'Refresh chart'
      )}
    </Button>
  ) : null

  const fullscreenBtn = onToggleFullscreen ? (
    <Button
      type="button"
      variant="outline-secondary"
      size="sm"
      id={`${idPrefix}-fullscreen`}
      className={compact ? 'py-0 px-2' : undefined}
      onClick={onToggleFullscreen}
      title={fullscreenActive ? 'Exit full screen' : 'Full screen chart'}
      aria-label={fullscreenActive ? 'Exit full screen' : 'Full screen chart'}
    >
      {fullscreenActive ? (compact ? 'Exit' : 'Exit full screen') : compact ? 'Full' : 'Full screen'}
    </Button>
  ) : null

  const horizontalEnabled = totalBars >= 2
  const verticalEnabled = totalBars >= 1

  const verticalAxisToolbar = verticalEnabled ? (
    <div className="d-flex align-items-center gap-1 flex-wrap">
      <span className={`small text-secondary text-uppercase ${compact ? '' : 'me-1'}`}>Y axis</span>
      <ButtonGroup size="sm">
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-y-zoom-in`}
          disabled={!(verticalZoomScale > PRICE_VERTICAL_ZOOM_MIN_SCALE + 1e-9)}
          onClick={onVerticalZoomIn}
          title="Y zoom in (narrower price band around midpoint)"
          aria-label="Y axis zoom in"
        >
          +
        </Button>
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-y-zoom-out`}
          disabled={!(verticalZoomScale < 1 - PRICE_VERTICAL_ZOOM_EPS)}
          onClick={onVerticalZoomOut}
          title="Y zoom out (toward auto price range)"
          aria-label="Y axis zoom out"
        >
          −
        </Button>
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-y-zoom-reset`}
          disabled={!(verticalZoomScale < 1 - PRICE_VERTICAL_ZOOM_EPS)}
          onClick={onVerticalZoomReset}
          title="Reset Y-axis zoom (full auto price range)"
          aria-label="Reset Y axis zoom"
        >
          Reset
        </Button>
      </ButtonGroup>
      {verticalZoomScale < 1 - PRICE_VERTICAL_ZOOM_EPS ? (
        <span className="small text-muted" style={{ fontSize: compact ? '0.7rem' : undefined }}>
          ~{Math.max(3, Math.min(98, Math.round(verticalZoomScale * 100)))}% band
        </span>
      ) : null}
    </div>
  ) : null

  const showing = visibleBarCount ?? totalBars
  const horizontalZoomed = horizontalEnabled && visibleBarCount != null && visibleBarCount < totalBars
  const windowPctApprox =
    horizontalZoomed && visibleBarCount != null
      ? Math.max(1, Math.min(99, Math.round((visibleBarCount / totalBars) * 100)))
      : null

  const horizontalToolbar = horizontalEnabled ? (
    <div className="d-flex align-items-center gap-1 flex-wrap">
      <span className={`small text-secondary text-uppercase ${compact ? '' : 'me-1'}`}>X axis</span>
      <ButtonGroup size="sm">
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-x-zoom-in`}
          disabled={!(showing > CHART_ZOOM_MIN_BARS)}
          onClick={onHorizontalZoomIn}
          title="X zoom in (narrower candle window)"
          aria-label="X axis zoom in"
        >
          +
        </Button>
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-x-zoom-out`}
          disabled={!horizontalZoomed}
          onClick={onHorizontalZoomOut}
          title="X zoom out (wider candle window)"
          aria-label="X axis zoom out"
        >
          −
        </Button>
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-x-zoom-reset`}
          disabled={!horizontalZoomed}
          onClick={onHorizontalZoomReset}
          title="Reset X-axis zoom (full downloaded series)"
          aria-label="Reset X axis zoom"
        >
          Reset
        </Button>
      </ButtonGroup>
      {horizontalZoomed ? (
        <span className="small text-muted" style={{ fontSize: compact ? '0.7rem' : undefined }}>
          ~{windowPctApprox}% · {visibleBarCount} / {totalBars}
        </span>
      ) : null}
    </div>
  ) : null

  if (!horizontalEnabled && !verticalEnabled && !fullscreenBtn && !refreshBtn) return null

  if (!horizontalEnabled) {
    if (!verticalAxisToolbar && !fullscreenBtn && !refreshBtn) return null
    return (
      <div className={`d-flex flex-wrap align-items-center gap-2 ${compact ? 'mb-1' : 'mb-2'}`}>
        {verticalAxisToolbar}
        {refreshBtn}
        {fullscreenBtn}
      </div>
    )
  }

  return (
    <div className={`d-flex flex-wrap align-items-center gap-2 gap-md-3 ${compact ? 'mb-1' : 'mb-2'}`}>
      {horizontalToolbar}
      {verticalAxisToolbar}
      {refreshBtn}
      {fullscreenBtn}
    </div>
  )
}
