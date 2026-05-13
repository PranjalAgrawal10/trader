import { Button, ButtonGroup, Spinner } from 'react-bootstrap'
import { CHART_ZOOM_MIN_BARS } from '../utils/chartZoom'

/** Fewer bars visible (most recent on the right); reindexed for chart axes. */
export function ChartZoomControls({
  idPrefix,
  totalBars,
  visibleBarCount,
  onZoomIn,
  onZoomOut,
  onReset,
  compact,
  onToggleFullscreen,
  fullscreenActive,
  onRefreshChart,
  chartRefreshing,
}: {
  idPrefix: string
  totalBars: number
  visibleBarCount: number | null
  onZoomIn: () => void
  onZoomOut: () => void
  onReset: () => void
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

  if (totalBars < 2) {
    if (!fullscreenBtn && !refreshBtn) return null
    return (
      <div className={`d-flex flex-wrap align-items-center gap-2 ${compact ? 'mb-1' : 'mb-2'}`}>
        {refreshBtn}
        {fullscreenBtn}
      </div>
    )
  }

  const showing = visibleBarCount ?? totalBars
  const zoomed = visibleBarCount != null && visibleBarCount < totalBars
  const windowPctApprox =
    zoomed && visibleBarCount != null
      ? Math.max(1, Math.min(99, Math.round((visibleBarCount / totalBars) * 100)))
      : null
  const canZoomIn = showing > CHART_ZOOM_MIN_BARS
  const canZoomOut = zoomed
  const canReset = zoomed
  return (
    <div className={`d-flex flex-wrap align-items-center gap-2 ${compact ? 'mb-1' : 'mb-2'}`}>
      <span className={`small text-secondary text-uppercase ${compact ? '' : 'me-1'}`}>Zoom</span>
      <ButtonGroup size="sm">
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-zoom-in`}
          disabled={!canZoomIn}
          onClick={onZoomIn}
          title="Zoom in (narrower window on downloaded history)"
          aria-label="Zoom chart in"
        >
          +
        </Button>
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-zoom-out`}
          disabled={!canZoomOut}
          onClick={onZoomOut}
          title="Zoom out (show more downloaded history)"
          aria-label="Zoom chart out"
        >
          −
        </Button>
        <Button
          type="button"
          variant="outline-secondary"
          id={`${idPrefix}-zoom-reset`}
          disabled={!canReset}
          onClick={onReset}
          title="Show full downloaded range (no horizontal zoom)"
          aria-label="Reset chart zoom"
        >
          Reset
        </Button>
      </ButtonGroup>
      {zoomed ? (
        <span className="small text-muted" style={{ fontSize: compact ? '0.7rem' : undefined }}>
          ~{windowPctApprox}% of loaded range ({visibleBarCount} / {totalBars} candles) · drag chart to pan
        </span>
      ) : null}
      {refreshBtn}
      {fullscreenBtn}
    </div>
  )
}
