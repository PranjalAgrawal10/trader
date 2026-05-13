import { Button, Spinner } from 'react-bootstrap'

/** Refresh + fullscreen only (horizontal/vertical candle zoom controls removed). */
export function ChartZoomControls({
  idPrefix,
  compact,
  onToggleFullscreen,
  fullscreenActive,
  onRefreshChart,
  chartRefreshing,
}: {
  idPrefix: string
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

  if (!fullscreenBtn && !refreshBtn) return null

  return (
    <div className={`d-flex flex-wrap align-items-center gap-2 ${compact ? 'mb-1' : 'mb-2'}`}>
      {refreshBtn}
      {fullscreenBtn}
    </div>
  )
}
