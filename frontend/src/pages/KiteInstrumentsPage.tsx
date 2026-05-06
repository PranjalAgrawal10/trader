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
  Nav,
  Row,
  Spinner,
  Table,
  ToggleButton,
} from 'react-bootstrap'
import {
  Bar,
  BarChart,
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

interface KiteFavoritesResponse {
  items: KiteInstrumentRow[]
}

const CHART_INTERVALS = ['1m', '2m', '3m', '4m', '5m', '10m', '15m', '30m', '1h', '1d'] as const
type ChartInterval = (typeof CHART_INTERVALS)[number]

/** Lookback for historical request. Labels mirror candle-style shorthand (5m = last 5 minutes); `1mo` = last calendar month. `auto` = omit from/to (server default per interval). */
const CHART_RANGE_PRESETS = [
  'auto',
  'last5m',
  'last10m',
  'last15m',
  'last30m',
  'last1h',
  'last5h',
  'last10h',
  'last1d',
  'last2d',
  'last5d',
  'last1mo',
] as const
type ChartRangePreset = (typeof CHART_RANGE_PRESETS)[number]

const CHART_RANGE_LABEL: Record<ChartRangePreset, string> = {
  auto: 'Auto',
  last5m: '5m',
  last10m: '10m',
  last15m: '15m',
  last30m: '30m',
  last1h: '1h',
  last5h: '5h',
  last10h: '10h',
  last1d: '1d',
  last2d: '2d',
  last5d: '5d',
  last1mo: '1mo',
}

function historicalRangeQueryParams(preset: ChartRangePreset): { from?: string; to?: string } {
  if (preset === 'auto') return {}
  const to = new Date()
  const from = new Date(to.getTime())
  switch (preset) {
    case 'last5m':
      from.setUTCMinutes(from.getUTCMinutes() - 5)
      break
    case 'last10m':
      from.setUTCMinutes(from.getUTCMinutes() - 10)
      break
    case 'last15m':
      from.setUTCMinutes(from.getUTCMinutes() - 15)
      break
    case 'last30m':
      from.setUTCMinutes(from.getUTCMinutes() - 30)
      break
    case 'last1h':
      from.setUTCHours(from.getUTCHours() - 1)
      break
    case 'last5h':
      from.setUTCHours(from.getUTCHours() - 5)
      break
    case 'last10h':
      from.setUTCHours(from.getUTCHours() - 10)
      break
    case 'last1d':
      from.setUTCDate(from.getUTCDate() - 1)
      break
    case 'last2d':
      from.setUTCDate(from.getUTCDate() - 2)
      break
    case 'last5d':
      from.setUTCDate(from.getUTCDate() - 5)
      break
    case 'last1mo':
      from.setUTCMonth(from.getUTCMonth() - 1)
      break
  }
  return { from: from.toISOString(), to: to.toISOString() }
}

type ChartGraphType = 'line' | 'bar'

type MainTab = 'browse' | 'favorites'

function favoriteRowKey(r: KiteInstrumentRow): string {
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
  enableKiteLiveSearch = true,
  favoriteKeySet,
  onToggleFavorite,
}: {
  title: string
  rows: KiteInstrumentRow[]
  truncated: boolean
  loading: boolean
  emptyHint: string
  searchSegment: 'fno' | 'mcx'
  selectedRowKey: string | null
  onSelectRow: (row: KiteInstrumentRow) => void
  enableKiteLiveSearch?: boolean
  favoriteKeySet: Set<string>
  onToggleFavorite: (row: KiteInstrumentRow) => void
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
    if (!enableKiteLiveSearch) return
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
  }, [search, loading, liveSearchLoading, localHasMatchForTypedQuery, searchSegment, enableKiteLiveSearch])

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
            {enableKiteLiveSearch ? (
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
            ) : null}
          </InputGroup>
        </Form.Group>
      </Form>
      <p className="small text-secondary mb-2">
        {!enableKiteLiveSearch
          ? combinedLoading
            ? 'Loading…'
            : rows.length === 0
              ? emptyHint
              : totalFiltered === 0
                ? 'No matches — try a different search.'
                : showing >= totalFiltered
                  ? `Showing all ${totalFiltered.toLocaleString()} favorite${totalFiltered === 1 ? '' : 's'}.`
                  : `Showing ${showing.toLocaleString()} of ${totalFiltered.toLocaleString()} favorites. Scroll for more.`
          : combinedLoading && !liveSearchLoading
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
                <th className="text-center" style={{ width: '2.5rem' }} title="Favorite">
                  ★
                </th>
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
                  className={favoriteRowKey(r) === selectedRowKey ? 'table-active' : undefined}
                  style={{ cursor: 'pointer' }}
                  onClick={() => onSelectRow(r)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault()
                      onSelectRow(r)
                    }
                  }}
                >
                  <td
                    className="text-center align-middle"
                    onClick={(e) => {
                      e.stopPropagation()
                      onToggleFavorite(r)
                    }}
                  >
                    <Button
                      type="button"
                      variant="link"
                      className="p-0 text-warning text-decoration-none lh-1"
                      aria-label={
                        favoriteKeySet.has(favoriteRowKey(r))
                          ? 'Remove from favorites'
                          : 'Add to favorites'
                      }
                      aria-pressed={favoriteKeySet.has(favoriteRowKey(r))}
                      tabIndex={0}
                      style={{ fontSize: '1.1rem' }}
                      onKeyDown={(e) => e.stopPropagation()}
                    >
                      {favoriteKeySet.has(favoriteRowKey(r)) ? '★' : '☆'}
                    </Button>
                  </td>
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

const CHART_MARGINS = { top: 4, right: 8, left: 0, bottom: 0 }

type ChartPoint = { idx: number; t: string; close: number; ohlc: string }

/** Re-fetch OHLC while a chart is mounted (browser tab visible) to keep the series current. */
const CHART_LIVE_POLL_MS = 30_000

function historicalCandlesToChartPoints(data: HistoricalCandlesResponse): ChartPoint[] {
  return data.candles.map((c, idx) => ({
    idx: idx + 1,
    t: c.time,
    close: Number(c.close),
    ohlc: `O ${c.open}  H ${c.high}  L ${c.low}  C ${c.close}  V ${c.volume}`,
  }))
}

type CandleRangeMeta = { interval: string; from: string; to: string }

function HistoricalRangeCaption({
  candleInterval,
  fromIso,
  toIso,
  compact,
}: {
  candleInterval: string
  fromIso: string
  toIso: string
  compact?: boolean
}) {
  const from = new Date(fromIso).toLocaleString()
  const to = new Date(toIso).toLocaleString()
  return (
    <div className={compact ? 'small text-secondary mb-2' : 'small text-secondary mb-3'}>
      <div className={compact ? 'mb-0' : 'mb-1'}>
        <strong className="text-body-secondary">From</strong>{' '}
        <span className="font-monospace">{from}</span>
      </div>
      <div className={compact ? 'mb-0' : 'mb-1'}>
        <strong className="text-body-secondary">To</strong>{' '}
        <span className="font-monospace">{to}</span>
      </div>
      <div className="text-muted" style={{ fontSize: compact ? '0.72rem' : '0.78rem' }}>
        Candle interval: {candleInterval}
      </div>
    </div>
  )
}

function ChartTooltipContent({
  active,
  payload,
}: {
  active?: boolean
  payload?: readonly { payload?: ChartPoint }[]
}) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload
  if (!p) return null
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
}

function ChartSettingsToolbar({
  idPrefix,
  rangePreset,
  onRangePresetChange,
  interval,
  onIntervalChange,
  graphType,
  onGraphTypeChange,
}: {
  idPrefix: string
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
}) {
  return (
    <>
      <div className="mb-3 d-flex flex-wrap align-items-center gap-2">
        <span className="small text-secondary text-uppercase me-1">Range</span>
        <ButtonGroup size="sm" className="flex-wrap">
          {CHART_RANGE_PRESETS.map((id) => (
            <ToggleButton
              key={id}
              id={`${idPrefix}-range-${id}`}
              type="radio"
              variant="outline-secondary"
              name={`${idPrefix}-chart-range`}
              value={id}
              checked={rangePreset === id}
              onChange={() => onRangePresetChange(id)}
              title={
                id === 'auto'
                  ? 'Server default window per candle size'
                  : id === 'last1mo'
                    ? 'Last calendar month (UTC)'
                    : 'From / to sent as UTC to the API'
              }
            >
              {CHART_RANGE_LABEL[id]}
            </ToggleButton>
          ))}
        </ButtonGroup>
      </div>
      <div className="mb-3 d-flex flex-wrap align-items-center gap-2">
        <span className="small text-secondary text-uppercase me-1">Interval</span>
        <ButtonGroup size="sm" className="flex-wrap">
          {CHART_INTERVALS.map((iv) => (
            <ToggleButton
              key={iv}
              id={`${idPrefix}-iv-${iv}`}
              type="radio"
              variant="outline-secondary"
              name={`${idPrefix}-chart-interval`}
              value={iv}
              checked={interval === iv}
              onChange={() => onIntervalChange(iv)}
            >
              {iv}
            </ToggleButton>
          ))}
        </ButtonGroup>
      </div>
      <div className="mb-3 d-flex flex-wrap align-items-center gap-2">
        <span className="small text-secondary text-uppercase me-1">Graph</span>
        <ButtonGroup size="sm">
          <ToggleButton
            id={`${idPrefix}-graph-line`}
            type="radio"
            variant="outline-secondary"
            name={`${idPrefix}-chart-graph`}
            value="line"
            checked={graphType === 'line'}
            onChange={() => onGraphTypeChange('line')}
          >
            Line
          </ToggleButton>
          <ToggleButton
            id={`${idPrefix}-graph-bar`}
            type="radio"
            variant="outline-secondary"
            name={`${idPrefix}-chart-graph`}
            value="bar"
            checked={graphType === 'bar'}
            onChange={() => onGraphTypeChange('bar')}
          >
            Bar
          </ToggleButton>
        </ButtonGroup>
      </div>
    </>
  )
}

function CompactPriceChart({
  row,
  rangePreset,
  interval,
  graphType,
  heightPx,
}: {
  row: KiteInstrumentRow
  rangePreset: ChartRangePreset
  interval: ChartInterval
  graphType: ChartGraphType
  heightPx: number
}) {
  const [series, setSeries] = useState<ChartPoint[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)

  useEffect(() => {
    const ac = new AbortController()
    setLoading(true)
    setError(null)
    setCandleRange(null)

    const fetchOnce = async (initial: boolean) => {
      const params = {
        instrumentToken: row.instrumentToken,
        interval,
        ...historicalRangeQueryParams(rangePreset),
      }
      try {
        const { data } = await api.get<HistoricalCandlesResponse>('/broker/kite/historical-candles', {
          params,
          signal: ac.signal,
        })
        const pts = historicalCandlesToChartPoints(data)
        setSeries(pts)
        setCandleRange({ interval: data.interval, from: data.from, to: data.to })
        if (initial) setError(null)
      } catch (err: unknown) {
        if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
        if (initial) {
          setSeries([])
          setCandleRange(null)
          setError(problemDetail(err))
        }
      } finally {
        if (initial && !ac.signal.aborted) setLoading(false)
      }
    }

    void fetchOnce(true)

    const timer = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return
      void fetchOnce(false)
    }, CHART_LIVE_POLL_MS)

    return () => {
      window.clearInterval(timer)
      ac.abort()
    }
  }, [row.instrumentToken, interval, rangePreset])

  return (
    <>
      {candleRange && !loading && !error ? (
        <HistoricalRangeCaption
          compact
          candleInterval={candleRange.interval}
          fromIso={candleRange.from}
          toIso={candleRange.to}
        />
      ) : null}
      <div style={{ height: heightPx }}>
        {error ? (
          <Alert variant="danger" className="py-1 small mb-0">
            {error}
          </Alert>
        ) : loading ? (
          <div className="d-flex align-items-center justify-content-center gap-2 text-secondary small h-100">
            <Spinner animation="border" size="sm" role="status" />
            Loading…
          </div>
        ) : series.length === 0 ? (
          <p className="text-secondary small mb-0 text-center py-4">No candles.</p>
        ) : (
          <ResponsiveContainer width="100%" height="100%">
            {graphType === 'line' ? (
              <LineChart data={series} margin={CHART_MARGINS}>
                <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 9 }} hide />
                <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} domain={['auto', 'auto']} width={48} />
                <Tooltip content={ChartTooltipContent} />
                <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} />
              </LineChart>
            ) : (
              <BarChart data={series} margin={CHART_MARGINS}>
                <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 9 }} hide />
                <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} domain={['auto', 'auto']} width={48} />
                <Tooltip content={ChartTooltipContent} />
                <Bar dataKey="close" fill="#0d6efd" maxBarSize={32} radius={[2, 2, 0, 0]} />
              </BarChart>
            )}
          </ResponsiveContainer>
        )}
      </div>
    </>
  )
}

function FavoritesChartsGrid({
  favorites,
  rangePreset,
  onRangePresetChange,
  interval,
  onIntervalChange,
  graphType,
  onGraphTypeChange,
  onToggleFavorite,
}: {
  favorites: KiteInstrumentRow[]
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
  onToggleFavorite: (r: KiteInstrumentRow) => void
}) {
  if (favorites.length === 0) return null

  return (
    <div className="mt-4">
      <h2 className="h6 mb-1">All charts</h2>
      <p className="small text-secondary mb-3">
        Historical close for every favorite below. Interval and graph type apply to all tiles. Charts re-fetch about
        every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this browser tab is visible.
      </p>
      <ChartSettingsToolbar
        idPrefix="fav-all"
        rangePreset={rangePreset}
        onRangePresetChange={onRangePresetChange}
        interval={interval}
        onIntervalChange={onIntervalChange}
        graphType={graphType}
        onGraphTypeChange={onGraphTypeChange}
      />
      <Row className="g-3">
        {favorites.map((row) => (
          <Col key={favoriteRowKey(row)} xs={12} lg={6} xl={4}>
            <Card className="border-secondary h-100">
              <Card.Body className="d-flex flex-column small py-3">
                <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
                  <div className="text-truncate">
                    <span className="font-monospace fw-semibold">{row.tradingsymbol}</span>
                    <span className="text-secondary ms-1">{row.exchange}</span>
                  </div>
                  <Button
                    type="button"
                    variant="outline-warning"
                    size="sm"
                    className="py-0 px-2 text-nowrap"
                    onClick={() => onToggleFavorite(row)}
                  >
                    ★ Remove
                  </Button>
                </div>
                <CompactPriceChart
                  row={row}
                  rangePreset={rangePreset}
                  interval={interval}
                  graphType={graphType}
                  heightPx={220}
                />
              </Card.Body>
            </Card>
          </Col>
        ))}
      </Row>
    </div>
  )
}

function InstrumentChartCard({
  selection,
  rangePreset,
  onRangePresetChange,
  interval,
  onIntervalChange,
  graphType,
  onGraphTypeChange,
  isFavorite,
  onToggleFavorite,
}: {
  selection: KiteInstrumentRow | null
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
  isFavorite: boolean
  onToggleFavorite?: () => void
}) {
  const [series, setSeries] = useState<ChartPoint[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)

  useEffect(() => {
    if (!selection) {
      setSeries([])
      setCandleRange(null)
      setError(null)
      setLoading(false)
      return
    }

    const ac = new AbortController()
    setLoading(true)
    setError(null)

    const fetchOnce = async (initial: boolean) => {
      const params = {
        instrumentToken: selection.instrumentToken,
        interval,
        ...historicalRangeQueryParams(rangePreset),
      }
      try {
        const { data } = await api.get<HistoricalCandlesResponse>('/broker/kite/historical-candles', {
          params,
          signal: ac.signal,
        })
        const pts = historicalCandlesToChartPoints(data)
        setSeries(pts)
        setCandleRange({ interval: data.interval, from: data.from, to: data.to })
        if (initial) setError(null)
      } catch (err: unknown) {
        if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return
        if (initial) {
          setSeries([])
          setCandleRange(null)
          setError(problemDetail(err))
        }
      } finally {
        if (initial && !ac.signal.aborted) setLoading(false)
      }
    }

    void fetchOnce(true)

    const timer = window.setInterval(() => {
      if (document.visibilityState !== 'visible') return
      void fetchOnce(false)
    }, CHART_LIVE_POLL_MS)

    return () => {
      window.clearInterval(timer)
      ac.abort()
    }
  }, [selection, interval, rangePreset])

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
            <p className="small text-secondary mb-2 d-flex flex-wrap align-items-center gap-2">
              <span className="font-monospace">{selection.tradingsymbol}</span>
              <span>· {selection.exchange}</span>
              {onToggleFavorite ? (
                <Button
                  type="button"
                  variant="outline-warning"
                  size="sm"
                  className="py-0 px-2"
                  aria-label={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
                  aria-pressed={isFavorite}
                  onClick={() => onToggleFavorite()}
                >
                  {isFavorite ? '★ Favorited' : '☆ Add to fav'}
                </Button>
              ) : null}
            </p>
            {candleRange && !loading && !error ? (
              <HistoricalRangeCaption
                candleInterval={candleRange.interval}
                fromIso={candleRange.from}
                toIso={candleRange.to}
              />
            ) : null}
            <ChartSettingsToolbar
              idPrefix="browse-detail"
              rangePreset={rangePreset}
              onRangePresetChange={onRangePresetChange}
              interval={interval}
              onIntervalChange={onIntervalChange}
              graphType={graphType}
              onGraphTypeChange={onGraphTypeChange}
            />
            <p className="small text-muted mb-2" style={{ fontSize: '0.78rem' }}>
              Data refreshes about every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this tab is visible.
            </p>
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
                  {graphType === 'line' ? (
                    <LineChart data={series} margin={CHART_MARGINS}>
                      <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 10 }} hide />
                      <YAxis stroke="#adb5bd" tick={{ fontSize: 11 }} domain={['auto', 'auto']} width={56} />
                      <Tooltip content={ChartTooltipContent} />
                      <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} />
                    </LineChart>
                  ) : (
                    <BarChart data={series} margin={CHART_MARGINS}>
                      <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 10 }} hide />
                      <YAxis stroke="#adb5bd" tick={{ fontSize: 11 }} domain={['auto', 'auto']} width={56} />
                      <Tooltip content={ChartTooltipContent} />
                      <Bar dataKey="close" fill="#0d6efd" maxBarSize={48} radius={[2, 2, 0, 0]} />
                    </BarChart>
                  )}
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
  const [favorites, setFavorites] = useState<KiteInstrumentRow[]>([])
  const [favoritesError, setFavoritesError] = useState<string | null>(null)

  const loadFavorites = useCallback(async () => {
    try {
      const { data } = await api.get<KiteFavoritesResponse>('/broker/kite/favorites')
      setFavorites(data.items)
      setFavoritesError(null)
    } catch (err) {
      setFavorites([])
      setFavoritesError(problemDetail(err))
    }
  }, [])

  const favoriteKeySet = useMemo(() => new Set(favorites.map(favoriteRowKey)), [favorites])

  const toggleFavorite = useCallback(
    async (r: KiteInstrumentRow) => {
      const key = favoriteRowKey(r)
      const exists = favorites.some((x) => favoriteRowKey(x) === key)
      setFavoritesError(null)
      try {
        if (exists) {
          await api.delete('/broker/kite/favorites', { params: { instrumentToken: r.instrumentToken } })
        } else {
          await api.post('/broker/kite/favorites', r)
        }
        await loadFavorites()
      } catch (err) {
        setFavoritesError(problemDetail(err))
      }
    },
    [favorites, loadFavorites],
  )

  const [mainTab, setMainTab] = useState<MainTab>('browse')
  const [provider, setProvider] = useState<string | null>(null)
  const [statusLoading, setStatusLoading] = useState(true)
  const [instruments, setInstruments] = useState<InstrumentsResponse | null>(null)
  const [instrumentsLoading, setInstrumentsLoading] = useState(false)
  const [instrumentsError, setInstrumentsError] = useState<string | null>(null)
  const [chartRow, setChartRow] = useState<KiteInstrumentRow | null>(null)
  const [chartInterval, setChartInterval] = useState<ChartInterval>('5m')
  const [chartRangePreset, setChartRangePreset] = useState<ChartRangePreset>('auto')
  const [chartGraphType, setChartGraphType] = useState<ChartGraphType>('line')

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

  useEffect(() => {
    if (!isZerodha) {
      setFavorites([])
      setFavoritesError(null)
      return
    }
    void loadFavorites()
  }, [isZerodha, loadFavorites])

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
                  server. Favorites are saved to <strong>your account on the server</strong> (not just this browser). Use the{' '}
                  <strong>star column</strong> (☆ / ★); open <strong>All favorites</strong> for the list and a{' '}
                  <strong>chart per favorite</strong>. On <strong>Browse</strong>, <strong>click a row</strong> for the
                  detailed price chart below.
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

            <Nav
              variant="tabs"
              className="mt-3 small"
              activeKey={mainTab}
              onSelect={(k) => k && setMainTab(k as MainTab)}
            >
              <Nav.Item>
                <Nav.Link eventKey="browse">Browse</Nav.Link>
              </Nav.Item>
              <Nav.Item>
                <Nav.Link eventKey="favorites">All favorites ({favorites.length})</Nav.Link>
              </Nav.Item>
            </Nav>

            {favoritesError ? (
              <Alert variant="danger" className="mt-2 py-2 small mb-0">
                Favorites: {favoritesError}
              </Alert>
            ) : null}

            {instrumentsError ? (
              <Alert variant="danger" className="mt-3 mb-0">
                {instrumentsError}
              </Alert>
            ) : null}

            {mainTab === 'browse' ? (
              <>
                <InstrumentListPanel
                  title="Futures & options (NFO / BFO)"
                  rows={instruments?.fno ?? EMPTY_INSTRUMENTS}
                  truncated={instruments?.fnoTruncated ?? false}
                  loading={instrumentsLoading}
                  emptyHint="No rows returned. Try Refresh or check your Kite session."
                  searchSegment="fno"
                  selectedRowKey={chartRow ? favoriteRowKey(chartRow) : null}
                  onSelectRow={setChartRow}
                  favoriteKeySet={favoriteKeySet}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                />
                <InstrumentListPanel
                  title="Commodities (MCX)"
                  rows={instruments?.commodities ?? EMPTY_INSTRUMENTS}
                  truncated={instruments?.commoditiesTruncated ?? false}
                  loading={instrumentsLoading}
                  emptyHint="No rows returned. Try Refresh or check your Kite session."
                  searchSegment="mcx"
                  selectedRowKey={chartRow ? favoriteRowKey(chartRow) : null}
                  onSelectRow={setChartRow}
                  favoriteKeySet={favoriteKeySet}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                />
              </>
            ) : (
              <>
                <InstrumentListPanel
                  title="All favorites"
                  rows={favorites}
                  truncated={false}
                  loading={false}
                  emptyHint="No favorites yet. Open Browse and tap the star (☆) on any contract row."
                  searchSegment="fno"
                  enableKiteLiveSearch={false}
                  selectedRowKey={chartRow ? favoriteRowKey(chartRow) : null}
                  onSelectRow={setChartRow}
                  favoriteKeySet={favoriteKeySet}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                />
                <FavoritesChartsGrid
                  favorites={favorites}
                  rangePreset={chartRangePreset}
                  onRangePresetChange={setChartRangePreset}
                  interval={chartInterval}
                  onIntervalChange={setChartInterval}
                  graphType={chartGraphType}
                  onGraphTypeChange={setChartGraphType}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                />
              </>
            )}

            {mainTab === 'browse' ? (
              <InstrumentChartCard
                selection={chartRow}
                rangePreset={chartRangePreset}
                onRangePresetChange={setChartRangePreset}
                interval={chartInterval}
                onIntervalChange={setChartInterval}
                graphType={chartGraphType}
                onGraphTypeChange={setChartGraphType}
                isFavorite={chartRow ? favoriteKeySet.has(favoriteRowKey(chartRow)) : false}
                onToggleFavorite={
                  chartRow ? () => void toggleFavorite(chartRow) : undefined
                }
              />
            ) : null}
          </Card.Body>
        </Card>
      ) : null}
    </Layout>
  )
}
