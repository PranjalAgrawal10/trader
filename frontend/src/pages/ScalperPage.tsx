import axios from 'axios'
import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Alert,
  Badge,
  Button,
  ButtonGroup,
  Card,
  Col,
  Form,
  InputGroup,
  ListGroup,
  Row,
  Spinner,
} from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import { CandlestickChart } from '../components/CandlestickChart'
import { Layout } from '../components/Layout'
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
import type { ChartPointWithMa } from '../utils/movingAverages'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'

interface BrokerStatusResponse {
  connected: boolean
  connectedAt: string | null
  provider: string | null
}

interface KiteInstrumentRow {
  instrumentToken: string
  tradingsymbol: string
  exchange: string
  name: string | null
  instrumentType: string | null
  segment: string | null
  expiry: string | null
  strike: number | null
  lotSize: number | null
}

interface KiteFavoritesResponse {
  items: KiteInstrumentRow[]
}

interface KiteTradingLocksResponse {
  items: KiteInstrumentRow[]
}

interface InstrumentSearchResponse {
  items: KiteInstrumentRow[]
  scanTruncated: boolean
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

export function ScalperPage() {
  const [broker, setBroker] = useState<BrokerStatusResponse | null>(null)
  const [favorites, setFavorites] = useState<KiteInstrumentRow[]>([])
  const [locks, setLocks] = useState<KiteInstrumentRow[]>([])
  const [listsError, setListsError] = useState<string | null>(null)

  const [watchSource, setWatchSource] = useState<'locks' | 'favorites'>('locks')
  const watchList = watchSource === 'locks' ? locks : favorites

  const [selected, setSelected] = useState<KiteInstrumentRow | null>(null)
  const [interval, setInterval] = useState<ScalperInterval>('1m')
  const [rangePreset, setRangePreset] = useState<ScalperRange>('last30m')

  const [rawSeries, setRawSeries] = useState<ChartPointWithMa[]>([])
  const [candleMeta, setCandleMeta] = useState<{ interval: string; from: string; to: string } | null>(null)
  const [chartError, setChartError] = useState<string | null>(null)
  const [chartLoading, setChartLoading] = useState(false)

  const [searchQ, setSearchQ] = useState('')
  const [searchItems, setSearchItems] = useState<KiteInstrumentRow[]>([])
  const [searchBusy, setSearchBusy] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)

  const isZerodha = broker?.connected === true && (broker?.provider ?? '').toLowerCase() === 'zerodha'

  const live = useLiveMarketTick(selected?.instrumentToken ?? null, isZerodha && selected != null)

  const loadLists = useCallback(async () => {
    if (!isZerodha) {
      setFavorites([])
      setLocks([])
      setListsError(null)
      return
    }
    setListsError(null)
    try {
      const [f, l] = await Promise.all([
        api.get<KiteFavoritesResponse>('/broker/kite/favorites'),
        api.get<KiteTradingLocksResponse>('/broker/kite/trading-locks'),
      ])
      setFavorites(f.data.items)
      setLocks(l.data.items)
    } catch (e) {
      setListsError(problemDetail(e))
      setFavorites([])
      setLocks([])
    }
  }, [isZerodha])

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const { data } = await api.get<BrokerStatusResponse>('/broker/status')
        if (!cancelled) setBroker(data)
      } catch {
        if (!cancelled) setBroker({ connected: false, connectedAt: null, provider: null })
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    void loadLists()
  }, [loadLists])

  useEffect(() => {
    if (!selected && watchList.length > 0) setSelected(watchList[0] ?? null)
  }, [watchList, selected])

  useEffect(() => {
    const ac = new AbortController()
    void (async () => {
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
        const data = await fetchMergedHistoricalChartCandles(
          selected.instrumentToken,
          interval,
          range,
          ac.signal,
        )
        if (ac.signal.aborted) return
        setRawSeries(chartPointsFromHistorical(data))
        setCandleMeta({ interval: data.interval, from: data.from, to: data.to })
      } catch (err: unknown) {
        if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
        setRawSeries([])
        setCandleMeta(null)
        setChartError(problemDetail(err))
      } finally {
        if (!ac.signal.aborted) setChartLoading(false)
      }
    })()
    return () => ac.abort()
  }, [isZerodha, selected?.instrumentToken, interval, rangePreset])

  useEffect(() => {
    if (!isZerodha || !selected) return
    const id = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return
      void (async () => {
        try {
          const range = scalperRangeQueryParams(rangePreset)
          const data = await fetchMergedHistoricalChartCandles(
            selected.instrumentToken,
            interval,
            range,
          )
          setRawSeries(chartPointsFromHistorical(data))
          setCandleMeta({ interval: data.interval, from: data.from, to: data.to })
          setChartError(null)
        } catch (err: unknown) {
          setChartError(problemDetail(err))
        }
      })()
    }, SCALPER_POLL_MS)
    return () => window.clearInterval(id)
  }, [isZerodha, selected, interval, rangePreset])

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

  useEffect(() => {
    const q = searchQ.trim()
    if (q.length < 2) {
      setSearchItems([])
      setSearchBusy(false)
      return
    }
    const ac = new AbortController()
    setSearchBusy(true)
    const t = window.setTimeout(() => {
      void (async () => {
        try {
          const { data } = await api.get<InstrumentSearchResponse>('/broker/kite/instruments/search', {
            params: { q, segment: 'all' },
            signal: ac.signal,
          })
          if (!ac.signal.aborted) {
            setSearchItems(data.items.slice(0, 12))
            setSearchOpen(true)
          }
        } catch (err: unknown) {
          if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
          setSearchItems([])
        } finally {
          if (!ac.signal.aborted) setSearchBusy(false)
        }
      })()
    }, 300)
    return () => {
      window.clearTimeout(t)
      ac.abort()
    }
  }, [searchQ])

  return (
    <Layout>
      <div className="d-flex flex-wrap align-items-baseline justify-content-between gap-2 mb-2">
        <div>
          <h1 className="h3 mb-0">Scalper</h1>
          <p className="text-secondary small mb-0">
            Tight-interval candles and live LTP for quick reads. Uses your trading locks or favorites as a watchlist.
          </p>
        </div>
        <div className="d-flex flex-wrap align-items-center gap-2">
          <Link to="/instruments" className="btn btn-sm btn-outline-secondary">
            Kite instruments
          </Link>
          <Button type="button" variant="outline-primary" size="sm" disabled={!isZerodha} onClick={() => void loadLists()}>
            Refresh list
          </Button>
        </div>
      </div>

      {!isZerodha ? (
        <Alert variant="warning" className="py-2">
          Connect Zerodha under <Link to="/profile#brokers">Profile → Brokers</Link> to use the scalper view.
        </Alert>
      ) : null}

      {listsError ? <Alert variant="warning">{listsError}</Alert> : null}

      <Row className="g-3">
        <Col lg={4} xl={3}>
          <Card className="border-secondary h-100">
            <Card.Header className="py-2 small d-flex flex-wrap align-items-center justify-content-between gap-2">
              <span className="fw-semibold">Watchlist</span>
              <ButtonGroup size="sm">
                <Button
                  variant={watchSource === 'locks' ? 'info' : 'outline-info'}
                  onClick={() => setWatchSource('locks')}
                >
                  Locks <Badge bg="dark" className="ms-1">{locks.length}</Badge>
                </Button>
                <Button
                  variant={watchSource === 'favorites' ? 'warning' : 'outline-warning'}
                  onClick={() => setWatchSource('favorites')}
                >
                  Fav <Badge bg="dark" className="ms-1">{favorites.length}</Badge>
                </Button>
              </ButtonGroup>
            </Card.Header>
            <Card.Body className="p-2">
              <div className="position-relative mb-2">
                <InputGroup size="sm">
                  <Form.Control
                    type="search"
                    placeholder="Search symbol…"
                    value={searchQ}
                    onChange={(e) => setSearchQ(e.target.value)}
                    onFocus={() => setSearchOpen(true)}
                    aria-label="Search instruments"
                  />
                  {searchBusy ? (
                    <InputGroup.Text>
                      <Spinner animation="border" size="sm" />
                    </InputGroup.Text>
                  ) : null}
                </InputGroup>
                {searchOpen && searchItems.length > 0 ? (
                  <ListGroup className="position-absolute w-100 shadow-sm mt-1" style={{ zIndex: 10 }}>
                    {searchItems.map((r) => (
                      <ListGroup.Item
                        key={`${r.exchange}:${r.instrumentToken}`}
                        action
                        className="small py-2"
                        onClick={() => {
                          setSelected(r)
                          setSearchOpen(false)
                          setSearchQ('')
                        }}
                      >
                        <span className="font-monospace">{r.tradingsymbol}</span>
                        <span className="text-muted"> · {r.exchange}</span>
                      </ListGroup.Item>
                    ))}
                  </ListGroup>
                ) : null}
              </div>

              {watchList.length === 0 ? (
                <p className="small text-muted mb-0">
                  No instruments in this list. Add them from{' '}
                  <Link to="/instruments">Instruments</Link> (favorites / locks).
                </p>
              ) : (
                <ListGroup variant="flush">
                  {watchList.map((r) => {
                    const active = selected?.instrumentToken === r.instrumentToken
                    return (
                      <ListGroup.Item
                        key={r.instrumentToken}
                        action
                        active={active}
                        className="small py-2 px-2 d-flex justify-content-between align-items-center"
                        onClick={() => setSelected(r)}
                      >
                        <span className="font-monospace text-truncate me-2">{r.tradingsymbol}</span>
                        <span className="text-muted text-nowrap">{r.exchange}</span>
                      </ListGroup.Item>
                    )
                  })}
                </ListGroup>
              )}
            </Card.Body>
          </Card>
        </Col>

        <Col lg={8} xl={9}>
          <Card className="border-secondary">
            <Card.Header className="py-2 d-flex flex-wrap align-items-center gap-2 justify-content-between">
              <div className="d-flex flex-wrap align-items-center gap-2">
                {selected ? (
                  <>
                    <span className="fw-semibold font-monospace">{selected.tradingsymbol}</span>
                    <Badge bg="secondary">{selected.exchange}</Badge>
                    {live.lastPrice != null ? (
                      <span className="font-monospace fs-5 text-success">LTP {live.lastPrice}</span>
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
                  <span className="text-muted small">Select a symbol from the list or search.</span>
                )}
              </div>
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
            </Card.Header>
            <Card.Body className="p-2">
              {chartError ? <Alert variant="danger" className="py-2 small mb-2">{chartError}</Alert> : null}
              {candleMeta ? (
                <div className="small text-muted mb-2 font-monospace">
                  {candleMeta.interval} · {formatLocalDateTime(candleMeta.from)} → {formatLocalDateTime(candleMeta.to)} ·
                  refresh ~{SCALPER_POLL_MS / 1000}s + ticks
                </div>
              ) : null}

              {chartLoading && rawSeries.length === 0 ? (
                <div className="text-center py-5 text-secondary">
                  <Spinner animation="border" className="me-2" />
                  Loading candles…
                </div>
              ) : selected && displaySeries.length > 0 ? (
                <div style={{ height: 'min(62vh, 560px)', minHeight: '320px' }}>
                  <CandlestickChart
                    data={displaySeries}
                    maLineVisibility={SCALPER_MA}
                    customEmaPeriod={null}
                    livePrice={live.lastPrice}
                  />
                </div>
              ) : selected && !chartLoading ? (
                <p className="text-muted small mb-0">No candle data returned for this range.</p>
              ) : !selected ? (
                <p className="text-muted small mb-0">Choose an instrument to load the chart.</p>
              ) : null}
            </Card.Body>
          </Card>
        </Col>
      </Row>
    </Layout>
  )
}
