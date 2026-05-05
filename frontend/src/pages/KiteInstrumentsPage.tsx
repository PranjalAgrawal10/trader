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
  Card,
  Col,
  Form,
  InputGroup,
  Row,
  Spinner,
  Table,
} from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
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
}: {
  title: string
  rows: KiteInstrumentRow[]
  truncated: boolean
  loading: boolean
  emptyHint: string
  searchSegment: 'fno' | 'mcx'
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
                <tr key={`${r.exchange}:${r.tradingsymbol}:${r.instrumentToken}`}>
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

const EMPTY_INSTRUMENTS: KiteInstrumentRow[] = []

export function KiteInstrumentsPage() {
  const [provider, setProvider] = useState<string | null>(null)
  const [statusLoading, setStatusLoading] = useState(true)
  const [instruments, setInstruments] = useState<InstrumentsResponse | null>(null)
  const [instrumentsLoading, setInstrumentsLoading] = useState(false)
  const [instrumentsError, setInstrumentsError] = useState<string | null>(null)

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
          Connect <strong>Zerodha (Kite)</strong> on the{' '}
          <Link to="/brokers" className="alert-link">
            Broker
          </Link>{' '}
          page to load instrument lists.
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
                  server.
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
            />
            <InstrumentListPanel
              title="Commodities (MCX)"
              rows={instruments?.commodities ?? EMPTY_INSTRUMENTS}
              truncated={instruments?.commoditiesTruncated ?? false}
              loading={instrumentsLoading}
              emptyHint="No rows returned. Try Refresh or check your Kite session."
              searchSegment="mcx"
            />
          </Card.Body>
        </Card>
      ) : null}
    </Layout>
  )
}
