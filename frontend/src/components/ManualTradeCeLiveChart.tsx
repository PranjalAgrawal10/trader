import axios from 'axios'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, Badge, Button, ButtonGroup, Card, Col, Form, Row, Spinner } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import { CandlestickChart } from './CandlestickChart'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import {
  chartPointsFromHistorical,
  mergeScalperLiveIntoSeries,
  pctChange,
  SCALPER_INTERVALS,
  SCALPER_MA,
  SCALPER_POLL_MS,
  SCALPER_RANGES,
  scalperRangeQueryParams,
  type ScalperInterval,
  type ScalperRange,
} from '../utils/scalperChartHelpers'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
import type { ChartPointWithMa } from '../utils/movingAverages'

export interface ManualTradeCeChartInstrument {
  instrumentToken: string
  tradingsymbol: string
  exchange: string
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

/** Live candlestick strip (scalper-style) scoped to CE locks — used on Manual paper trade. */
export function ManualTradeCeLiveChart({
  isZerodha,
  ceLocks,
}: {
  isZerodha: boolean
  ceLocks: ManualTradeCeChartInstrument[]
}) {
  const [chartToken, setChartToken] = useState<string>('')
  const [interval, setInterval] = useState<ScalperInterval>('1m')
  const [rangePreset, setRangePreset] = useState<ScalperRange>('last30m')
  const [rawSeries, setRawSeries] = useState<ChartPointWithMa[]>([])
  const [candleMeta, setCandleMeta] = useState<{ interval: string; from: string; to: string } | null>(null)
  const [chartError, setChartError] = useState<string | null>(null)
  const [chartLoading, setChartLoading] = useState(false)

  const selected = useMemo(
    () => ceLocks.find((r) => r.instrumentToken === chartToken) ?? null,
    [ceLocks, chartToken],
  )

  useEffect(() => {
    if (ceLocks.length === 0) {
      setChartToken('')
      return
    }
    setChartToken((t) => (t && ceLocks.some((c) => c.instrumentToken === t) ? t : ceLocks[0]!.instrumentToken))
  }, [ceLocks])

  const live = useLiveMarketTick(selected?.instrumentToken ?? null, isZerodha && selected != null)

  const reload = useCallback(
    async (signal?: AbortSignal) => {
      if (!isZerodha || !selected) {
        setRawSeries([])
        setCandleMeta(null)
        setChartLoading(false)
        setChartError(null)
        return
      }
      setChartLoading(true)
      setChartError(null)
      try {
        const range = scalperRangeQueryParams(rangePreset)
        const data = await fetchMergedHistoricalChartCandles(selected.instrumentToken, interval, range, signal)
        if (signal?.aborted) return
        setRawSeries(chartPointsFromHistorical(data))
        setCandleMeta({ interval: data.interval, from: data.from, to: data.to })
      } catch (err: unknown) {
        if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
        setRawSeries([])
        setCandleMeta(null)
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
  }, [reload])

  useEffect(() => {
    if (!isZerodha || !selected) return
    const id = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return
      void reload()
    }, SCALPER_POLL_MS)
    return () => window.clearInterval(id)
  }, [isZerodha, selected, interval, rangePreset, reload])

  const displaySeries = useMemo(
    () => mergeScalperLiveIntoSeries(rawSeries, live.lastTick, interval),
    [rawSeries, live.lastTick, interval],
  )

  const liveVsBar = useMemo(() => {
    const last = live.lastPrice
    const ref = rawSeries.length > 0 ? rawSeries[rawSeries.length - 1]?.close : null
    if (ref == null || last == null || !Number.isFinite(ref) || !Number.isFinite(last)) return null
    return pctChange(ref, last)
  }, [live.lastPrice, rawSeries])

  if (!isZerodha) return null

  if (ceLocks.length === 0) {
    return (
      <Card className="border-secondary">
        <Card.Header className="py-2 small fw-semibold">Live CE chart (scalper)</Card.Header>
        <Card.Body className="py-3">
          <p className="small text-secondary mb-2">
            No <strong>call (CE)</strong> contracts in <strong>Locked for trading</strong>. Add CE option locks to see
            live candles here, or use the full{' '}
            <Link to="/scalper">Scalper</Link> page for any symbol.
          </p>
          <Link to="/instruments?tab=locked" className="btn btn-outline-secondary btn-sm">
            Open locks
          </Link>
        </Card.Body>
      </Card>
    )
  }

  return (
    <Card className="border-secondary">
      <Card.Header className="py-2 d-flex flex-wrap align-items-center gap-2 justify-content-between">
        <div className="d-flex flex-wrap align-items-center gap-2">
          <span className="small fw-semibold text-uppercase text-secondary letter-spacing-1">Live CE (scalper)</span>
          {selected ? (
            <>
              <Badge bg="secondary">CE</Badge>
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
          ) : null}
        </div>
        <Link to="/scalper" className="btn btn-outline-secondary btn-sm">
          Full scalper
        </Link>
      </Card.Header>
      <Card.Body className="p-2 p-md-3">
        <Row className="g-2 align-items-end mb-2">
          <Col xs={12} md>
            <Form.Group controlId="manual-trade-ce-symbol" className="mb-0">
              <Form.Label className="small text-secondary mb-1">CE lock</Form.Label>
              <Form.Select
                size="sm"
                value={chartToken}
                onChange={(e) => setChartToken(e.target.value)}
                aria-label="Select call option lock for chart"
              >
                {ceLocks.map((r) => (
                  <option key={r.instrumentToken} value={r.instrumentToken}>
                    {r.tradingsymbol} ({r.exchange})
                  </option>
                ))}
              </Form.Select>
            </Form.Group>
          </Col>
          <Col xs={12} md="auto" className="d-flex flex-wrap gap-1 align-items-center">
            <ButtonGroup size="sm">
              {SCALPER_INTERVALS.map((iv) => (
                <Button
                  key={iv}
                  variant={interval === iv ? 'primary' : 'outline-primary'}
                  onClick={() => setInterval(iv)}
                >
                  {iv}
                </Button>
              ))}
            </ButtonGroup>
            <ButtonGroup size="sm">
              {SCALPER_RANGES.map((r) => (
                <Button
                  key={r.id}
                  variant={rangePreset === r.id ? 'secondary' : 'outline-secondary'}
                  onClick={() => setRangePreset(r.id)}
                >
                  {r.label}
                </Button>
              ))}
            </ButtonGroup>
          </Col>
        </Row>

        {chartError ? <Alert variant="danger" className="py-2 small mb-2">{chartError}</Alert> : null}
        {candleMeta ? (
          <div className="small text-muted mb-2 font-monospace">
            {candleMeta.interval} · {formatLocalDateTime(candleMeta.from)} → {formatLocalDateTime(candleMeta.to)} · poll
            ~{SCALPER_POLL_MS / 1000}s + ticks
          </div>
        ) : null}

        {chartLoading && rawSeries.length === 0 ? (
          <div className="text-center py-4 text-secondary small">
            <Spinner animation="border" size="sm" className="me-2" />
            Loading candles…
          </div>
        ) : selected && displaySeries.length > 0 ? (
          <div style={{ height: 'min(48vh, 440px)', minHeight: '260px' }}>
            <CandlestickChart data={displaySeries} maLineVisibility={SCALPER_MA} customEmaPeriod={null} />
          </div>
        ) : selected && !chartLoading ? (
          <p className="text-muted small mb-0">No candle data for this range.</p>
        ) : null}
      </Card.Body>
    </Card>
  )
}
