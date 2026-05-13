import axios from 'axios'
import type { ReactNode } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Alert, Badge, Card, Col, Form, Row, Spinner } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { ReferenceLine } from 'recharts'
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import { CHART_FULLSCREEN_META_WRAP_CLASS, CHART_FULLSCREEN_META_WRAP_STYLE } from '../constants/chartLayout'
import { useChartFullscreen } from '../hooks/useChartFullscreen'
import { useChartPanPointerHandlers } from '../hooks/useChartPanPointerHandlers'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import { useMlChartPredictionEntries } from '../hooks/useMlChartPredictionEntries'
import { chartDataIndicesForPaperBuyMarkers } from '../utils/demoPaperBuyBarMarkers'
import {
  CHART_LIVE_POLL_MS,
  historicalRangeQueryParams,
  type ChartGraphType,
  type ChartInterval,
  type ChartRangePreset,
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
import {
  clampChartPanAllowNewerGhost,
  correctedChartZoomStored,
  sliceChartForZoom,
  visibleBarsFromChartZoomStored,
  zoomInChartZoomStored,
  zoomOutChartZoomStored,
} from '../utils/chartZoom'
import {
  applyVerticalPriceZoomToDomain,
  zoomInVerticalPriceScale,
  zoomOutVerticalPriceScale,
} from '../utils/chartVerticalZoom'
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
  return 'Request failed.'
}

function effectiveCustomEmaPeriod(visibility: MaLineVisibility, period: number): number | null {
  if (!visibility.showCustomEma) return null
  const n = Math.floor(period)
  if (!Number.isFinite(n) || n < CUSTOM_EMA_PERIOD_MIN || n > CUSTOM_EMA_PERIOD_MAX) return null
  return n
}

type CandleRangeMeta = { interval: string; from: string; to: string }

/**
 * Manual paper-trade chart uses the same Kite merged-OHLC pipeline, zoom/pan, refresh cadence,
 * and toolbar settings as favorite tiles (Instruments → Favorites “All charts”).
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
    rangePreset: ChartRangePreset
    interval: ChartInterval
    graphType: ChartGraphType
    maLineVisibility: MaLineVisibility
    customEmaPeriod: number
    chartZoomStored: number | null
    onChartZoomStoredChange: (stored: number | null) => void
    demoPaperBuyMarkers: readonly DemoPaperOpenBuyMarkerDto[]
  }
  /** Range / interval / graph / indicators; shown in fullscreen scroll strip with caption + zoom. */
  chartFullscreenToolbar?: ReactNode
}) {
  const {
    rangePreset,
    interval,
    graphType,
    maLineVisibility,
    customEmaPeriod,
    chartZoomStored,
    onChartZoomStoredChange,
    demoPaperBuyMarkers,
  } = kiteChart

  const [series, setSeries] = useState<ChartPointWithMa[]>([])
  const seriesSourceRef = useRef<ChartPointWithMa[] | null>(null)
  const liveVolAccRef = useRef<LiveTickVolumeAccumulator>({ lastCumulativeVolume: null })
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)
  const [chartError, setChartError] = useState<string | null>(null)
  const [chartLoading, setChartLoading] = useState(false)
  const [chartRefreshTick, setChartRefreshTick] = useState(0)
  const [chartPanOffsetBars, setChartPanOffsetBars] = useState(0)
  const [priceVerticalZoomScale, setPriceVerticalZoomScale] = useState(1)

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
        const extra = historicalRangeQueryParams(rangePreset)
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
    [isZerodha, selected, interval, rangePreset],
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
  }, [isZerodha, selected, interval, rangePreset, reload])

  useEffect(() => {
    setPriceVerticalZoomScale(1)
  }, [selected?.instrumentToken])

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

  const zoomVisibleBars = useMemo(
    () => visibleBarsFromChartZoomStored(chartZoomStored, seriesWithCustom.length),
    [chartZoomStored, seriesWithCustom.length],
  )

  useEffect(() => {
    if (chartZoomStored == null || seriesWithCustom.length === 0) return
    const next = correctedChartZoomStored(chartZoomStored, seriesWithCustom.length)
    if (next === undefined) return
    if (next === chartZoomStored) return
    onChartZoomStoredChange(next)
  }, [chartZoomStored, seriesWithCustom.length, onChartZoomStoredChange])

  useEffect(() => {
    setChartPanOffsetBars(0)
  }, [chartZoomStored, selected?.instrumentToken])

  useEffect(() => {
    setChartPanOffsetBars((p) => clampChartPanAllowNewerGhost(p, seriesWithCustom.length, zoomVisibleBars))
  }, [seriesWithCustom.length, zoomVisibleBars])

  const chartPanEnabled =
    zoomVisibleBars != null && seriesWithCustom.length > zoomVisibleBars && seriesWithCustom.length > 1

  const { panPointerProps } = useChartPanPointerHandlers({
    enabled: chartPanEnabled,
    totalBars: seriesWithCustom.length,
    visibleBarCount: zoomVisibleBars,
    maxNewerGhostBars: zoomVisibleBars ?? 0,
    panOffsetBars: chartPanOffsetBars,
    setPanOffsetBars: setChartPanOffsetBars,
  })

  const { style: chartPanPointerStyle, ...chartPanPointerHandlers } = panPointerProps

  const chartData = useMemo(
    () => sliceChartForZoom(seriesWithCustom, zoomVisibleBars, chartPanOffsetBars),
    [seriesWithCustom, zoomVisibleBars, chartPanOffsetBars],
  )

  const paperBuyDataIndices = useMemo(
    () => [...chartDataIndicesForPaperBuyMarkers(demoPaperBuyMarkers ?? [], chartData, interval as ChartIntervalKey)],
    [demoPaperBuyMarkers, chartData, interval],
  )

  const paperBuyReferenceLines = useMemo(() => {
    return paperBuyDataIndices.map((di, seg) => {
      const rowPt = chartData[di]
      if (!rowPt || !selected) return null
      return (
        <ReferenceLine
          key={`demo-pbuy-${selected.instrumentToken}-${seg}-${rowPt.t}`}
          x={rowPt.idx}
          stroke="#84cc16"
          strokeWidth={1.35}
          strokeDasharray="5 5"
          opacity={0.92}
        />
      )
    })
  }, [paperBuyDataIndices, chartData, selected])

  const paperLastBuyReferenceLine =
    paperLastBuyPrice != null && Number.isFinite(paperLastBuyPrice) ? (
      <ReferenceLine
        y={paperLastBuyPrice}
        stroke="#f59e0b"
        strokeWidth={1.5}
        strokeDasharray="4 6"
        label={{ value: 'Last buy', position: 'insideLeft', fill: '#f59e0b', fontSize: 9, fontWeight: 600 }}
      />
    ) : null

  const rechartsYDomain = useMemo(() => {
    const base = yDomainForOhlcAndVisibleMas(chartData, maLineVisibility)
    let d = extendYDomainWithLivePrice(base, paperLastBuyPrice ?? null)
    d = extendYDomainWithLivePrice(d, live.lastPrice)
    return applyVerticalPriceZoomToDomain(d ?? undefined, priceVerticalZoomScale) ?? d
  }, [chartData, maLineVisibility, paperLastBuyPrice, live.lastPrice, priceVerticalZoomScale])

  const { entries: mlPredictionEntries, reloadHistory: reloadMlHistory } = useMlChartPredictionEntries(
    selected?.instrumentToken ?? null,
    interval,
    seriesWithCustom,
  )

  useEffect(() => {
    if (!selected?.instrumentToken || series.length === 0) return
    void reloadMlHistory()
  }, [selected?.instrumentToken, interval, series, reloadMlHistory])

  const onChartZoomIn = useCallback(() => {
    onChartZoomStoredChange(zoomInChartZoomStored(chartZoomStored, seriesWithCustom.length))
  }, [onChartZoomStoredChange, chartZoomStored, seriesWithCustom.length])

  const onChartZoomOut = useCallback(() => {
    onChartZoomStoredChange(zoomOutChartZoomStored(chartZoomStored, seriesWithCustom.length))
  }, [onChartZoomStoredChange, chartZoomStored, seriesWithCustom.length])

  const onChartZoomReset = useCallback(() => onChartZoomStoredChange(null), [onChartZoomStoredChange])

  const onVerticalZoomIn = useCallback(
    () => setPriceVerticalZoomScale((v) => zoomInVerticalPriceScale(v)),
    [],
  )
  const onVerticalZoomOut = useCallback(
    () => setPriceVerticalZoomScale((v) => zoomOutVerticalPriceScale(v)),
    [],
  )
  const onVerticalZoomReset = useCallback(() => setPriceVerticalZoomScale(1), [])

  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()

  const manualChartZoomToolbar =
    selected != null ? (
      <ChartZoomControls
        idPrefix={`manual-trade-chart-${selected.instrumentToken}`}
        totalBars={series.length}
        visibleBarCount={zoomVisibleBars}
        onHorizontalZoomIn={onChartZoomIn}
        onHorizontalZoomOut={onChartZoomOut}
        onHorizontalZoomReset={onChartZoomReset}
        verticalZoomScale={priceVerticalZoomScale}
        onVerticalZoomIn={onVerticalZoomIn}
        onVerticalZoomOut={onVerticalZoomOut}
        onVerticalZoomReset={onVerticalZoomReset}
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
          <strong>Chart</strong> uses the same persisted <strong>Range</strong>, <strong>interval</strong>, line/bar/candle
          mode, <strong>MA / S&amp;R</strong> toggles, <strong>horizontal</strong>/<strong>vertical</strong> zoom, and
          refresh cadence as <strong>Browse</strong> / <strong>All favorites</strong>.
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
                ...chartPanPointerStyle,
              }}
              {...chartPanPointerHandlers}
            >
              <InstrumentPriceChart
                graphType={graphType}
                data={chartData}
                maLineVisibility={maLineVisibility}
                customEmaPeriod={customEmaApplied}
                livePrice={live.lastPrice ?? null}
                paperLastBuyPrice={paperLastBuyPrice ?? null}
                paperBuyDataIndices={paperBuyDataIndices}
                mlPredictionEntries={mlPredictionEntries}
                rechartsYDomain={rechartsYDomain ?? undefined}
                referenceLines={
                  <>
                    {paperBuyReferenceLines}
                    {paperLastBuyReferenceLine}
                  </>
                }
                density="compact"
                newerGhostBars={Math.max(0, -chartPanOffsetBars)}
                priceVerticalZoomScale={priceVerticalZoomScale}
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
