import axios from 'axios'
import type { ReactNode } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Alert, Badge, Card, Col, Form, Row, Spinner } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import {
  CHART_DEFAULT_VISIBLE_BARS,
  CHART_FULLSCREEN_META_WRAP_CLASS,
  CHART_FULLSCREEN_META_WRAP_STYLE,
} from '../constants/chartLayout'
import { useChartFullscreen } from '../hooks/useChartFullscreen'
import { useChartOlderBars } from '../hooks/useChartOlderBars'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import { chartDataIndicesForPaperBuyMarkers } from '../utils/demoPaperBuyBarMarkers'
import {
  CHART_LIVE_POLL_MS,
  historicalRangeQueryParams,
  type ChartGraphType,
  type ChartInterval,
} from '../utils/kiteInstrumentChartShared'
import type { ChartIntervalKey, LiveTickVolumeAccumulator } from '../utils/liveCandleMerge'
import { mergeLiveTickIntoOhlc } from '../utils/liveCandleMerge'
import {
  addCustomEmaToChartPoints,
  CUSTOM_EMA_PERIOD_MAX,
  CUSTOM_EMA_PERIOD_MIN,
  extendYDomainWithLivePrice,
  type ChartPointWithMa,
  type MaLineVisibility,
  yDomainForOhlcAndVisibleMas,
} from '../utils/movingAverages'
import {
  chartPointsFromHistorical,
  pctChange,
} from '../utils/scalperChartHelpers'
import { ChartZoomControls } from './ChartZoomControls'
import { HistoricalRangeCaption } from './HistoricalRangeCaption'
import { InstrumentPriceChart } from './InstrumentPriceChart'

export interface ManualTradeScalperInstrument {
  instrumentToken: string
  tradingsymbol: string
  exchange: string
  instrumentType?: string | null
}

export type DemoPaperOpenBuyMarkerDto = {
  boughtAtUtc: string
  contractsRemaining: number
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return err instanceof Error ? err.message : 'Request failed.'
}

function effectiveCustomEmaPeriod(visibility: MaLineVisibility, period: number): number | null {
  if (!visibility.showCustomEma) return null
  const n = Math.floor(period)
  if (!Number.isFinite(n) || n < CUSTOM_EMA_PERIOD_MIN || n > CUSTOM_EMA_PERIOD_MAX) return null
  return n
}

type CandleRangeMeta = { interval: string; from: string; to: string }

/** Fixed historical lookback for this chart (toolbar Range row is omitted on Manual trade). */
const MANUAL_TRADE_HISTORICAL_RANGE = 'last3d' as const

/**
 * Manual paper-trade scalper chart: merges live ticks like other tiles; always requests three calendar days of OHLC,
 * anchors the Lightweight range on the {@link CHART_DEFAULT_VISIBLE_BARS} newest candles, and loads older bars when you
 * scroll left toward the window start.
 */
export function ManualTradeScalperView({
  isZerodha,
  tradingLocks,
  selectedInstrumentToken,
  onSelectedInstrumentTokenChange,
  paperLastBuyPrice,
  kiteChart,
  chartFullscreenToolbar,
}: {
  isZerodha: boolean
  tradingLocks: ManualTradeScalperInstrument[]
  selectedInstrumentToken: string
  onSelectedInstrumentTokenChange: (instrumentToken: string) => void
  paperLastBuyPrice?: number | null
  kiteChart: {
    interval: ChartInterval
    graphType: ChartGraphType
    maLineVisibility: MaLineVisibility
    customEmaPeriod: number
    demoPaperBuyMarkers: readonly DemoPaperOpenBuyMarkerDto[]
  }
  /** Range / interval / graph / indicators; shown in fullscreen scroll strip with caption. */
  chartFullscreenToolbar?: ReactNode
}) {
  const {
    interval,
    graphType,
    maLineVisibility,
    customEmaPeriod,
    demoPaperBuyMarkers,
  } = kiteChart

  const [series, setSeries] = useState<ChartPointWithMa[]>([])
  const seriesSourceRef = useRef<ChartPointWithMa[] | null>(null)
  const liveVolAccRef = useRef<LiveTickVolumeAccumulator>({ lastCumulativeVolume: null })
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)
  const [chartError, setChartError] = useState<string | null>(null)
  const [chartLoading, setChartLoading] = useState(false)
  const [chartRefreshTick, setChartRefreshTick] = useState(0)

  const selected = useMemo(
    () => tradingLocks.find((r) => r.instrumentToken === selectedInstrumentToken) ?? null,
    [tradingLocks, selectedInstrumentToken],
  )

  const live = useLiveMarketTick(selected?.instrumentToken ?? null, isZerodha && selected != null)

  const reload = useCallback(
    async (signal?: AbortSignal) => {
      if (!isZerodha || !selected) {
        setSeries([])
        setCandleRange(null)
        setChartLoading(false)
        setChartError(null)
        return
      }
      setChartLoading(true)
      setChartError(null)
      try {
        const extra = historicalRangeQueryParams(MANUAL_TRADE_HISTORICAL_RANGE)
        const data = await fetchMergedHistoricalChartCandles(selected.instrumentToken, interval, extra, signal)
        if (signal?.aborted) return
        setSeries(chartPointsFromHistorical(data))
        setCandleRange({ interval: data.interval, from: data.from, to: data.to })
      } catch (err: unknown) {
        if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
        setSeries([])
        setCandleRange(null)
        setChartError(problemDetail(err))
      } finally {
        if (!signal?.aborted) setChartLoading(false)
      }
    },
    [isZerodha, selected, interval],
  )

  useEffect(() => {
    const ac = new AbortController()
    void reload(ac.signal)
    return () => ac.abort()
  }, [reload, chartRefreshTick])

  useEffect(() => {
    if (!isZerodha || !selected) return
    const id = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return
      void reload()
    }, CHART_LIVE_POLL_MS)
    return () => window.clearInterval(id)
  }, [isZerodha, selected, interval, reload])

  const customEmaApplied = useMemo(
    () => effectiveCustomEmaPeriod(maLineVisibility, customEmaPeriod),
    [maLineVisibility, customEmaPeriod],
  )

  const tickMergedSeries = useMemo(() => {
    if (seriesSourceRef.current !== series) {
      seriesSourceRef.current = series
      liveVolAccRef.current.lastCumulativeVolume = null
    }
    return mergeLiveTickIntoOhlc(
      series,
      live.lastTick,
      interval as ChartIntervalKey,
      graphType,
      liveVolAccRef.current,
    ) as ChartPointWithMa[]
  }, [series, live.lastTick, interval, graphType])

  const seriesWithCustom = useMemo(
    () => addCustomEmaToChartPoints(tickMergedSeries, customEmaApplied),
    [tickMergedSeries, customEmaApplied],
  )

  const { loadOlderBars, loadingOlderBars, canLoadOlderBars } = useChartOlderBars({
    instrumentToken: selected?.instrumentToken ?? '',
    interval,
    candleWindow: candleRange ? { from: candleRange.from, to: candleRange.to } : null,
    series,
    chartPointsFromMerged: chartPointsFromHistorical,
    setSeries,
  })

  const chartData = seriesWithCustom

  const paperBuyDataIndices = useMemo(
    () => [...chartDataIndicesForPaperBuyMarkers(demoPaperBuyMarkers ?? [], chartData, interval as ChartIntervalKey)],
    [demoPaperBuyMarkers, chartData, interval],
  )

  const rechartsYDomain = useMemo(() => {
    const base = yDomainForOhlcAndVisibleMas(chartData, maLineVisibility)
    let d = extendYDomainWithLivePrice(base, paperLastBuyPrice ?? null)
    d = extendYDomainWithLivePrice(d, live.lastPrice)
    return d
  }, [chartData, maLineVisibility, paperLastBuyPrice, live.lastPrice])

  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()

  const manualChartZoomToolbar =
    selected != null ? (
      <ChartZoomControls
        idPrefix={`manual-trade-chart-${selected.instrumentToken}`}
        compact
        onToggleFullscreen={toggleFullscreen}
        fullscreenActive={fullscreenActive}
        onRefreshChart={() => setChartRefreshTick((n) => n + 1)}
        chartRefreshing={chartLoading}
      />
    ) : null

  const liveVsBar = useMemo(() => {
    const last = live.lastPrice
    const ref = series.length > 0 ? series[series.length - 1]?.close : null
    if (ref == null || last == null || !Number.isFinite(ref) || !Number.isFinite(last)) return null
    return pctChange(ref, last)
  }, [live.lastPrice, series])

  if (!isZerodha) return null

  const typeLabel = selected?.instrumentType?.trim()
  const instLabel = manualTradeScalperFormatInstrumentLabel(selected)

  if (tradingLocks.length === 0) {
    return (
      <Card className="border-secondary">
        <Card.Header className="py-2 small fw-semibold">Scalper view</Card.Header>
        <Card.Body className="py-3">
          <p className="small text-secondary mb-2">
            No <strong>Locked for trading</strong> instruments yet. Add a lock to chart and paper-trade the same symbol
            here, or open the standalone{' '}
            <Link to="/scalper">Scalper</Link> page.
          </p>
          <Link to="/instruments?tab=locked" className="btn btn-outline-secondary btn-sm">
            Open locks
          </Link>
        </Card.Body>
      </Card>
    )
  }

  const chartHasData = !chartError && series.length > 0
  const metaOutside = !fullscreenActive || !chartHasData

  return (
    <Card className="border-secondary">
      <Card.Header className="py-2 d-flex flex-wrap align-items-center gap-2 justify-content-between">
        <div className="d-flex flex-wrap align-items-center gap-2">
          <span className="small fw-semibold text-uppercase text-secondary letter-spacing-1">Scalper view</span>
          {selected ? (
            <>
              {instLabel !== null ? <Badge bg="secondary">{instLabel}</Badge> : null}
              <span className="fw-semibold font-monospace small">{selected.tradingsymbol}</span>
              <Badge bg="dark">{selected.exchange}</Badge>
              {live.lastPrice != null ? (
                <span className="font-monospace text-success">LTP {live.lastPrice}</span>
              ) : (
                <span className="text-muted small">LTP —</span>
              )}
              {liveVsBar ? (
                <span className={`small ${liveVsBar.startsWith('+') ? 'text-success' : 'text-danger'}`}>
                  {liveVsBar} vs last bar
                </span>
              ) : null}
            </>
          ) : (
            <span className="text-muted small">Pick a locked symbol below.</span>
          )}
        </div>
        <Link to="/scalper" className="btn btn-outline-secondary btn-sm">
          Full scalper
        </Link>
      </Card.Header>
      <Card.Body className="p-2 p-md-3">
        <Row className="g-2 align-items-end mb-2">
          <Col xs={12} md>
            <Form.Group controlId="manual-trade-scalper-symbol" className="mb-0">
              <Form.Label className="small text-secondary mb-1">
                Locked instrument <span className="text-muted">(same as paper trade)</span>
              </Form.Label>
              <Form.Select
                size="sm"
                value={
                  tradingLocks.some((r) => r.instrumentToken === selectedInstrumentToken.trim())
                    ? selectedInstrumentToken
                    : tradingLocks[0]?.instrumentToken ?? ''
                }
                onChange={(e) => onSelectedInstrumentTokenChange(e.target.value)}
                aria-label="Locked instrument for scalper and paper trade"
              >
                {tradingLocks.map((r) => (
                  <option key={r.instrumentToken} value={r.instrumentToken}>
                    {r.tradingsymbol} ({r.exchange})
                  </option>
                ))}
              </Form.Select>
            </Form.Group>
          </Col>
        </Row>

        <p className="small text-muted mb-2 mb-md-3">
          <strong>Chart</strong> loads the last <strong>3 calendar days</strong> of candles; the opening view spans about the{' '}
          <strong>{CHART_DEFAULT_VISIBLE_BARS} newest</strong> bars (pan left for more inside the window — at the left edge
          it fetches older candles; wheel / pinch / axis drag can zoom). <strong>Interval</strong>, line/bar/candle mode, and{' '}
          <strong>MA / S&amp;R</strong> toggles stay synced with <strong>Browse</strong> / <strong>All favorites</strong>{' '}
          (toolbar: <strong>refresh</strong> + <strong>fullscreen</strong>; range is fixed for scalper speed).
        </p>

        {chartFullscreenToolbar && !fullscreenActive ? (
          <div className="mb-2">{chartFullscreenToolbar}</div>
        ) : null}

        {typeLabel?.length ? (
          <p className="small text-muted mb-2 mb-md-3">
            Instrument type <span className="font-monospace">{typeLabel}</span> · historical data refreshes about every{' '}
            {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this tab is visible + live ticks when open.
          </p>
        ) : (
          <p className="small text-muted mb-2 mb-md-3">
            Historical data refreshes about every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this tab is visible + live
            ticks when open.
          </p>
        )}

        {chartError ? <Alert variant="danger" className="py-2 small mb-2">{chartError}</Alert> : null}

        {metaOutside && candleRange && !chartError ? (
          <HistoricalRangeCaption
            compact
            candleInterval={candleRange.interval}
            fromIso={candleRange.from}
            toIso={candleRange.to}
          />
        ) : null}

        {chartLoading && series.length === 0 ? (
          <div className="text-center py-4 text-secondary small">
            <Spinner animation="border" size="sm" className="me-2" />
            Loading candles…
          </div>
        ) : selected && chartHasData ? (
          <div
            ref={panelRef}
            className={fullscreenActive ? 'd-flex flex-column' : undefined}
            style={
              fullscreenActive
                ? {
                    minHeight: '100vh',
                    height: '100%',
                    background: 'var(--bs-body-bg)',
                    padding: '0.35rem',
                  }
                : undefined
            }
          >
            {fullscreenActive ? (
              <div className={CHART_FULLSCREEN_META_WRAP_CLASS} style={CHART_FULLSCREEN_META_WRAP_STYLE}>
                {chartFullscreenToolbar}
                {candleRange && !chartError ? (
                  <HistoricalRangeCaption
                    compact
                    candleInterval={candleRange.interval}
                    fromIso={candleRange.from}
                    toIso={candleRange.to}
                  />
                ) : null}
                {manualChartZoomToolbar}
              </div>
            ) : null}
            {!fullscreenActive ? manualChartZoomToolbar : null}
            <div
              className={fullscreenActive ? 'flex-grow-1 w-100' : 'w-100'}
              style={{
                height: fullscreenActive ? '100%' : 'min(48vh, 440px)',
                minHeight: fullscreenActive ? 0 : '260px',
                flex: fullscreenActive ? '1 1 auto' : undefined,
              }}
            >
              <InstrumentPriceChart
                graphType={graphType}
                data={chartData}
                maLineVisibility={maLineVisibility}
                customEmaPeriod={customEmaApplied}
                livePrice={live.lastPrice ?? null}
                paperLastBuyPrice={paperLastBuyPrice ?? null}
                paperBuyDataIndices={paperBuyDataIndices}
                rechartsYDomain={rechartsYDomain ?? undefined}
                density="compact"
                newerGhostBars={0}
                onNeedOlderBars={loadOlderBars}
                canLoadOlderBars={canLoadOlderBars}
                loadingOlderBars={loadingOlderBars}
              />
            </div>
          </div>
        ) : selected && !chartLoading ? (
          <p className="text-muted small mb-0">No candle data for this range.</p>
        ) : !selected ? (
          <p className="text-muted small mb-0">Select an instrument.</p>
        ) : null}
      </Card.Body>
    </Card>
  )
}

function manualTradeScalperFormatInstrumentLabel(row: ManualTradeScalperInstrument | null): string | null {
  if (!row) return null
  const raw = row.instrumentType?.trim().toUpperCase()
  if (!raw) return null
  if (raw === 'EQ') return 'EQ'
  if (raw === 'CE') return 'CE'
  if (raw === 'PE') return 'PE'
  if (raw === 'FUT') return 'Fut'
  if (raw.length <= 12) return raw
  return raw.slice(0, 11) + '…'
}
