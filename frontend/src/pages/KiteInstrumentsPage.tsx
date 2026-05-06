import axios from 'axios'
import {
  useCallback,
  useDeferredValue,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from 'react'
import {
  Alert,
  Button,
  ButtonGroup,
  Card,
  Col,
  Form,
  InputGroup,
  Row,
  Spinner,
  Table,
  ToggleButton,
} from 'react-bootstrap'
import {
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { Layout } from '../components/Layout'

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

interface InstrumentsResponse {
  fno: KiteInstrumentRow[]
  commodities: KiteInstrumentRow[]
  fnoTruncated: boolean
  commoditiesTruncated: boolean
}

interface InstrumentSearchResponse {
  items: KiteInstrumentRow[]
  scanTruncated: boolean
}

interface HistoricalCandlesResponse {
  candles: {
    time: string
    open: number
    high: number
    low: number
    close: number
    volume: number
  }[]
  interval: string
  from: string
  to: string
}

const CHART_INTERVALS = ['1m', '2m', '3m', '4m', '5m', '10m', '15m', '30m', '1h', '1d'] as const
type ChartInterval = (typeof CHART_INTERVALS)[number]

function instrumentRowKey(r: KiteInstrumentRow): string {
  return `${r.exchange}:${r.tradingsymbol}:${r.instrumentToken}`
}

const INSTRUMENT_PAGE_SIZE = 100
const SCROLL_LOAD_THRESHOLD_PX = 96

function rowSearchHaystack(r: KiteInstrumentRow): string {
  return [
    r.tradingsymbol,
    r.name,
    r.exchange,
    r.instrumentType,
    r.segment,
    r.expiry,
    r.strike != null ? String(r.strike) : '',
    r.lotSize != null ? String(r.lotSize) : '',
    r.instrumentToken,
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase()
}

function InstrumentListPanel({
  title,
  rows,
  truncated,
  loading,
  emptyHint,
  searchSegment,
  selectedRowKey,
  onSelectRow,
}: {
  title: string
  rows: KiteInstrumentRow[]
  truncated: boolean
  loading: boolean
  emptyHint: string
  searchSegment: 'fno' | 'mcx'
  selectedRowKey: string | null
  onSelectRow: (row: KiteInstrumentRow) => void
}) {
  const searchFieldId = useId()
  const [search, setSearch] = useState('')
  const deferredSearch = useDeferredValue(search)
  const [visibleCount, setVisibleCount] = useState(INSTRUMENT_PAGE_SIZE)
  const scrollRef = useRef<HTMLDivElement>(null)

  const [serverMode, setServerMode] = useState(false)
  const [serverHits, setServerHits] = useState<KiteInstrumentRow[]>([])
  const [serverScanTruncated, setServerScanTruncated] = useState(false)
  const [liveSearchLoading, setLiveSearchLoading] = useState(false)
  const [liveSearchError, setLiveSearchError] = useState<string | null>(null)

  const filtered = useMemo(() => {
    if (serverMode) return serverHits
    const q = deferredSearch.trim().toLowerCase()
    if (!q) return rows
    return rows.filter((r) => rowSearchHaystack(r).includes(q))
  }, [rows, deferredSearch, serverMode, serverHits])

  const localHasMatchForTypedQuery = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return false
    return rows.some((r) => rowSearchHaystack(r).includes(q))
  }, [search, rows])

  useEffect(() => {
    setVisibleCount(INSTRUMENT_PAGE_SIZE)
    scrollRef.current?.scrollTo({ top: 0 })
  }, [filtered.length, deferredSearch, rows.length, serverMode, serverHits.length])

  const displayed = useMemo(
    () => filtered.slice(0, visibleCount),
    [filtered, visibleCount],
  )

  const onScroll = useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const { scrollTop, clientHeight, scrollHeight } = el
    if (scrollTop + clientHeight < scrollHeight - SCROLL_LOAD_THRESHOLD_PX) return
    setVisibleCount((v) => {
      if (v >= filtered.length) return v
      return Math.min(v + INSTRUMENT_PAGE_SIZE, filtered.length)
    })
  }, [filtered.length])

  const showing = displayed.length
  const totalFiltered = filtered.length

  const runLiveSearch = useCallback(async () => {
    const q = search.trim()
    if (!q || loading || liveSearchLoading) return
    if (localHasMatchForTypedQuery) return

    setLiveSearchLoading(true)
    setLiveSearchError(null)
    try {
      const { data } = await api.get<InstrumentSearchResponse>('/broker/kite/instruments/search', {
        params: { q, segment: searchSegment },
      })
      setServerHits(data.items)
      setServerScanTruncated(data.scanTruncated)
      setServerMode(true)
    } catch (err) {
      setLiveSearchError(problemDetail(err))
      setServerMode(false)
      setServerHits([])
      setServerScanTruncated(false)
    } finally {
      setLiveSearchLoading(false)
    }
  }, [search, loading, liveSearchLoading, localHasMatchForTypedQuery, searchSegment])

  const onSearchChange = (value: string) => {
    setSearch(value)
    if (serverMode || liveSearchError) {
      setServerMode(false)
      setServerHits([])
      setServerScanTruncated(false)
      setLiveSearchError(null)
    }
  }

  const combinedLoading = loading || liveSearchLoading

  return (
    <div className="mt-4">
      <h2 className="h6 mb-2">{title}</h2>
      {truncated && !serverMode ? (
        <Alert variant="warning" className="py-2 small mb-2">
          List may be incomplete (row cap applied when fetching).
        </Alert>
      ) : null}
      {serverMode && serverScanTruncated ? (
        <Alert variant="info" className="py-2 small mb-2">
          Showing up to the server match limit — more contracts may match on Kite.
        </Alert>
      ) : null}
      <Form
        onSubmit={(e) => {
          e.preventDefault()
          void runLiveSearch()
        }}
      >
        <Form.Group className="mb-2" controlId={searchFieldId}>
          <Form.Label className="small text-secondary text-uppercase">Search</Form.Label>
          <InputGroup size="sm">
            <Form.Control
              type="search"
              placeholder="Symbol, name, exchange, expiry…"
              value={search}
              onChange={(e) => onSearchChange(e.target.value)}
              disabled={loading && rows.length === 0}
              aria-label={`Search ${title}`}
              autoComplete="off"
            />
            <Button
              type="submit"
              variant="outline-secondary"
              disabled={
                combinedLoading || !search.trim() || localHasMatchForTypedQuery
              }
            >
              {liveSearchLoading ? (
                <>
                  <Spinner animation="border" size="sm" className="me-1" />
                  Kite…
                </>
              ) : (
                'Search Kite'
              )}
            </Button>
          </InputGroup>
        </Form.Group>
      </Form>
      <p className="small text-secondary mb-2">
        {combinedLoading && !liveSearchLoading
          ? 'Loading…'
          : liveSearchLoading
            ? 'Searching full Kite instrument files…'
            : rows.length === 0 && !serverMode
              ? emptyHint
              : serverMode && totalFiltered === 0
                ? 'No matches from Kite for this text.'
                : totalFiltered === 0
                  ? 'No matches in the preview list — press Enter or Search Kite to scan the full file.'
                  : showing >= totalFiltered
                    ? serverMode
                      ? `Full Kite search: ${totalFiltered.toLocaleString()} match${totalFiltered === 1 ? '' : 'es'}.`
                      : `Showing all ${totalFiltered.toLocaleString()} match${totalFiltered === 1 ? '' : 'es'} (${rows.length.toLocaleString()} in preview).`
                    : `Showing ${showing.toLocaleString()} of ${totalFiltered.toLocaleString()} match${totalFiltered === 1 ? '' : 'es'}. Scroll for 100 more.`}
      </p>
      {liveSearchError ? (
        <Alert variant="danger" className="py-2 small mb-2">
          {liveSearchError}
        </Alert>
      ) : null}
      <div
        ref={scrollRef}
        className="rounded border border-secondary"
        style={{ maxHeight: '24rem', overflow: 'auto' }}
        onScroll={onScroll}
      >
        {loading && rows.length === 0 && !serverMode ? (
          <div className="d-flex align-items-center gap-2 p-4 text-secondary small">
            <Spinner animation="border" size="sm" role="status" />
            Loading…
          </div>
        ) : rows.length === 0 && !serverMode ? (
          <p className="p-4 text-secondary small mb-0">{emptyHint}</p>
        ) : totalFiltered === 0 && !liveSearchLoading ? null : (
          <Table striped hover size="sm" className="mb-0 align-middle small">
            <thead className="table-dark sticky-top">
              <tr>
                <th>Symbol</th>
                <th>Exch.</th>
                <th>Type</th>
                <th>Segment</th>
                <th>Expiry</th>
                <th>Strike</th>
                <th>Lot</th>
              </tr>
            </thead>
            <tbody>
              {displayed.map((r) => (
                <tr
                  key={`${r.exchange}:${r.tradingsymbol}:${r.instrumentToken}`}
                  role="button"
                  tabIndex={0}
                  className={instrumentRowKey(r) === selectedRowKey ? 'table-active' : undefined}
                  style={{ cursor: 'pointer' }}
                  onClick={() => onSelectRow(r)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault()
                      onSelectRow(r)
                    }
                  }}
                >
                  <td className="font-monospace">{r.tradingsymbol}</td>
                  <td>{r.exchange}</td>
                  <td>{r.instrumentType ?? '—'}</td>
                  <td>{r.segment ?? '—'}</td>
                  <td>{r.expiry ?? '—'}</td>
                  <td>{r.strike != null ? r.strike : '—'}</td>
                  <td>{r.lotSize ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </div>
    </div>
  )
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

function InstrumentChartCard({
  selection,
  interval,
  onIntervalChange,
}: {
  selection: KiteInstrumentRow | null
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
}) {
  const [series, setSeries] = useState<{ idx: number; t: string; close: number; ohlc: string }[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [rangeHint, setRangeHint] = useState<string | null>(null)

  useEffect(() => {
    if (!selection) {
      setSeries([])
      setRangeHint(null)
      setError(null)
      setLoading(false)
      return
    }

    const ac = new AbortController()
    setLoading(true)
    setError(null)

    ;(async () => {
      try {
        const { data } = await api.get<HistoricalCandlesResponse>('/broker/kite/historical-candles', {
          params: { instrumentToken: selection.instrumentToken, interval },
          signal: ac.signal,
        })
        const pts = data.candles.map((c, idx) => ({
          idx: idx + 1,
          t: c.time,
          close: Number(c.close),
          ohlc: `O ${c.open}  H ${c.high}  L ${c.low}  C ${c.close}  V ${c.volume}`,
        }))
        setSeries(pts)
        setRangeHint(
          `${data.interval} · ${new Date(data.from).toLocaleString()} → ${new Date(data.to).toLocaleString()}`,
        )
      } catch (err: unknown) {
        if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
        setSeries([])
        setRangeHint(null)
        setError(problemDetail(err))
      } finally {
        if (!ac.signal.aborted) setLoading(false)
      }
    })()

    return () => ac.abort()
  }, [selection, interval])

  return (
    <Card className="border-secondary mt-4">
      <Card.Body>
        <Card.Title className="h6 mb-2">Price chart</Card.Title>
        {!selection ? (
          <p className="text-secondary small mb-0">
            Click a row in either list to plot closing prices from Kite (historical OHLCV).
          </p>
        ) : (
          <>
            <p className="small text-secondary mb-2">
              <span className="font-monospace">{selection.tradingsymbol}</span> · {selection.exchange}
              {rangeHint ? <span className="d-block mt-1">{rangeHint}</span> : null}
            </p>
            <div className="mb-3 d-flex flex-wrap align-items-center gap-2">
              <span className="small text-secondary text-uppercase me-1">Interval</span>
              <ButtonGroup size="sm" className="flex-wrap">
                {CHART_INTERVALS.map((iv) => (
                  <ToggleButton
                    key={iv}
                    id={`chart-iv-${iv}`}
                    type="radio"
                    variant="outline-secondary"
                    name="chart-interval"
                    value={iv}
                    checked={interval === iv}
                    onChange={() => onIntervalChange(iv)}
                  >
                    {iv}
                  </ToggleButton>
                ))}
              </ButtonGroup>
            </div>
            {error ? (
              <Alert variant="danger" className="py-2 small mb-2">
                {error}
              </Alert>
            ) : null}
            <div style={{ height: '18rem' }}>
              {loading ? (
                <div className="d-flex align-items-center gap-2 text-secondary small py-5 justify-content-center">
                  <Spinner animation="border" size="sm" role="status" />
                  Loading candles…
                </div>
              ) : series.length === 0 ? (
                <p className="text-secondary small mb-0 py-5 text-center">No candles returned for this range.</p>
              ) : (
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={series} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                    <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 10 }} hide />
                    <YAxis stroke="#adb5bd" tick={{ fontSize: 11 }} domain={['auto', 'auto']} width={56} />
                    <Tooltip
                      content={({ active, payload }) => {
                        if (!active || !payload?.length) return null
                        const p = payload[0].payload as { t: string; close: number; ohlc: string }
                        return (
                          <div
                            className="rounded border border-secondary p-2 small"
                            style={{ background: '#212529', color: '#f8f9fa' }}
                          >
                            <div>{new Date(p.t).toLocaleString()}</div>
                            <div className="font-monospace mt-1">Close {p.close}</div>
                            <div className="mt-1 text-secondary" style={{ fontSize: '0.78rem' }}>
                              {p.ohlc}
                            </div>
                          </div>
                        )
                      }}
                    />
                    <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} />
                  </LineChart>
                </ResponsiveContainer>
              )}
            </div>
          </>
        )}
      </Card.Body>
    </Card>
  )
}

const EMPTY_INSTRUMENTS: KiteInstrumentRow[] = []

export function KiteInstrumentsPage() {
  const [provider, setProvider] = useState<string | null>(null)
  const [statusLoading, setStatusLoading] = useState(true)
  const [instruments, setInstruments] = useState<InstrumentsResponse | null>(null)
  const [instrumentsLoading, setInstrumentsLoading] = useState(false)
  const [instrumentsError, setInstrumentsError] = useState<string | null>(null)
  const [chartRow, setChartRow] = useState<KiteInstrumentRow | null>(null)
  const [chartInterval, setChartInterval] = useState<ChartInterval>('5m')

  const loadStatus = useCallback(async () => {
    setStatusLoading(true)
    try {
      const { data } = await api.get<BrokerStatusResponse>('/broker/status')
      setProvider(data.provider ?? null)
    } catch {
      setProvider(null)
    } finally {
      setStatusLoading(false)
    }
  }, [])

  const isZerodha = provider?.toLowerCase() === 'zerodha'

  const loadInstruments = useCallback(async () => {
    if (!isZerodha) {
      setInstruments(null)
      setInstrumentsError(null)
      setInstrumentsLoading(false)
      return
    }
    setInstrumentsLoading(true)
    setInstrumentsError(null)
    try {
      const { data } = await api.get<InstrumentsResponse>('/broker/kite/instruments/fno-commodities')
      setInstruments(data)
    } catch (err) {
      setInstruments(null)
      setInstrumentsError(problemDetail(err))
    } finally {
      setInstrumentsLoading(false)
    }
  }, [isZerodha])

  useEffect(() => {
    void loadStatus()
  }, [loadStatus])

  useEffect(() => {
    void loadInstruments()
  }, [loadInstruments])

  return (
    <Layout>
      <h1 className="h3 mb-1">F&O & commodities</h1>
      <p className="text-secondary small mb-4" style={{ maxWidth: '42rem' }}>
        Master contract rows from Kite for NFO, BFO, and MCX (daily instrument dump; not live quotes).
      </p>

      {statusLoading ? (
        <p className="text-secondary small">Loading broker status…</p>
      ) : !isZerodha ? (
        <Alert variant="info" className="mb-4">
          Connect <strong>Zerodha (Kite)</strong> under <strong>Profile</strong> →{' '}
          <Link to={`/profile#${BROKER_PROFILE_SECTION_ID}`} className="alert-link">
            Broker connection
          </Link>{' '}
          to load instrument lists.
        </Alert>
      ) : null}

      {isZerodha ? (
        <Card className="border-secondary">
          <Card.Body>
            <Row className="align-items-start g-3">
              <Col>
                <Card.Title className="h5 mb-0">Kite instruments</Card.Title>
                <Card.Text className="text-secondary small mt-2 mb-0">
                  Preview loads 100 rows per exchange. Filter as you type. If nothing matches the preview, press{' '}
                  <strong>Enter</strong> or <strong>Search Kite</strong> to scan the full instrument file on the
                  server. <strong>Click a row</strong> to plot historical closes (interval buttons apply to the chart).
                </Card.Text>
              </Col>
              <Col xs={12} md="auto">
                <Button
                  variant="outline-secondary"
                  disabled={instrumentsLoading}
                  onClick={() => void loadInstruments()}
                >
                  {instrumentsLoading ? 'Refreshing…' : 'Refresh lists'}
                </Button>
              </Col>
            </Row>

            {instrumentsError ? (
              <Alert variant="danger" className="mt-3 mb-0">
                {instrumentsError}
              </Alert>
            ) : null}

            <InstrumentListPanel
              title="Futures & options (NFO / BFO)"
              rows={instruments?.fno ?? EMPTY_INSTRUMENTS}
              truncated={instruments?.fnoTruncated ?? false}
              loading={instrumentsLoading}
              emptyHint="No rows returned. Try Refresh or check your Kite session."
              searchSegment="fno"
              selectedRowKey={chartRow ? instrumentRowKey(chartRow) : null}
              onSelectRow={setChartRow}
            />
            <InstrumentListPanel
              title="Commodities (MCX)"
              rows={instruments?.commodities ?? EMPTY_INSTRUMENTS}
              truncated={instruments?.commoditiesTruncated ?? false}
              loading={instrumentsLoading}
              emptyHint="No rows returned. Try Refresh or check your Kite session."
              searchSegment="mcx"
              selectedRowKey={chartRow ? instrumentRowKey(chartRow) : null}
              onSelectRow={setChartRow}
            />

            <InstrumentChartCard
              selection={chartRow}
              interval={chartInterval}
              onIntervalChange={setChartInterval}
            />
          </Card.Body>
        </Card>
      ) : null}
    </Layout>
  )
}
