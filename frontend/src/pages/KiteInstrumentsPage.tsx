import axios from 'axios'
import {
  Fragment,
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
  ComposedChart,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { Layout } from '../components/Layout'
import { CandlestickChart } from '../components/CandlestickChart'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import type { MarketTickBatchItem } from '../services/marketHub'
import type { ChartPointOhlc } from '../utils/liveCandleMerge'
import { mergeLiveTickIntoOhlc } from '../utils/liveCandleMerge'
import {
  CHART_ZOOM_MIN_BARS,
  sliceChartForZoom,
  zoomInBarCount,
  zoomOutBarCount,
} from '../utils/chartZoom'
import {
  appendMlPrediction,
  historiesEqual,
  loadMlHistory,
  resolveMlHistory,
  saveMlHistory,
  type MlPredictionLogEntry,
} from '../utils/mlPredictionHistory'
import {
  addCustomEmaToChartPoints,
  attachMovingAverages,
  CUSTOM_EMA_DEFAULT_PERIOD,
  CUSTOM_EMA_PERIOD_MAX,
  CUSTOM_EMA_PERIOD_MIN,
  DEFAULT_MA_LINE_VISIBILITY,
  MA_EMA_FAST_PERIOD,
  MA_EMA_SLOW_PERIOD,
  MA_LINE_COLORS,
  MA_SMA_PERIOD,
  yDomainForOhlcAndVisibleMas,
  type ChartPointWithMa,
  type MaLineVisibility,
} from '../utils/movingAverages'

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
    sma20?: number | null
    ema9?: number | null
    ema21?: number | null
  }[]
  interval: string
  from: string
  to: string
}

/** GET /api/v1/predictions/price-direction */
interface PriceDirectionApiResponse {
  direction: 'up' | 'down' | 'neutral'
  confidence: number
  modelId: string
  detail: string
}

interface KiteFavoritesResponse {
  items: KiteInstrumentRow[]
}

interface KiteInstrumentsChartSettingsDto {
  interval: string | null
  rangePreset: string | null
  graphType: string | null
  zoomByInstrumentToken?: Record<string, number> | null
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

type ChartGraphType = 'line' | 'bar' | 'candlestick'

function coerceChartInterval(v: string | null | undefined): ChartInterval {
  if (v && (CHART_INTERVALS as readonly string[]).includes(v)) return v as ChartInterval
  return '5m'
}

function coerceChartRangePreset(v: string | null | undefined): ChartRangePreset {
  if (v && (CHART_RANGE_PRESETS as readonly string[]).includes(v)) return v as ChartRangePreset
  return 'auto'
}

function coerceChartGraphType(v: string | null | undefined): ChartGraphType {
  if (v === 'bar' || v === 'line' || v === 'candlestick') return v
  return 'line'
}

type MainTab = 'browse' | 'favorites'

/** Deep-link: <code>?tab=favorites</code>, <code>?tab=fav</code>, <code>?fav=1</code>, or <code>?fav=true</code>. */
function favoritesTabFromSearchParams(params: URLSearchParams): boolean {
  const tab = params.get('tab')
  const fav = params.get('fav')
  return (
    tab === 'favorites' ||
    tab === 'fav' ||
    fav === '1' ||
    (fav != null && fav.toLowerCase() === 'true')
  )
}

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

const CUSTOM_EMA_PREFS_STORAGE_KEY = 'trader.kiteChart.customEma.v1'

function loadCustomEmaPrefs(): { period: number; show: boolean } {
  try {
    const raw = window.localStorage.getItem(CUSTOM_EMA_PREFS_STORAGE_KEY)
    if (!raw) return { period: CUSTOM_EMA_DEFAULT_PERIOD, show: false }
    const j = JSON.parse(raw) as { period?: unknown; show?: unknown }
    const periodRaw = Math.round(Number(j.period))
    const period = Number.isFinite(periodRaw)
      ? Math.min(CUSTOM_EMA_PERIOD_MAX, Math.max(CUSTOM_EMA_PERIOD_MIN, periodRaw))
      : CUSTOM_EMA_DEFAULT_PERIOD
    return { period, show: Boolean(j.show) }
  } catch {
    return { period: CUSTOM_EMA_DEFAULT_PERIOD, show: false }
  }
}

function saveCustomEmaPrefs(prefs: { period: number; show: boolean }): void {
  try {
    window.localStorage.setItem(CUSTOM_EMA_PREFS_STORAGE_KEY, JSON.stringify(prefs))
  } catch {
    /* quota / private mode */
  }
}

/** When toggle is on and period is valid, return integer period for series; otherwise <c>null</c> (skip column). */
function effectiveCustomEmaPeriod(visibility: MaLineVisibility, period: number): number | null {
  if (!visibility.showCustomEma) return null
  const n = Math.floor(period)
  if (!Number.isFinite(n) || n < CUSTOM_EMA_PERIOD_MIN || n > CUSTOM_EMA_PERIOD_MAX) return null
  return n
}

const CHART_MARGINS = { top: 4, right: 8, left: 0, bottom: 0 }

/** Fewer bars visible (most recent on the right); reindexed for chart axes. */
function ChartZoomControls({
  idPrefix,
  totalBars,
  visibleBarCount,
  onZoomIn,
  onZoomOut,
  onReset,
  compact,
}: {
  idPrefix: string
  totalBars: number
  visibleBarCount: number | null
  onZoomIn: () => void
  onZoomOut: () => void
  onReset: () => void
  compact?: boolean
}) {
  if (totalBars < 2) return null
  const showing = visibleBarCount ?? totalBars
  const zoomed = visibleBarCount != null && visibleBarCount < totalBars
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
          title="Zoom in (fewer bars, latest on the right)"
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
          title="Zoom out"
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
          title="Show full downloaded range"
          aria-label="Reset chart zoom"
        >
          Reset
        </Button>
      </ButtonGroup>
      {zoomed ? (
        <span className="small text-muted" style={{ fontSize: compact ? '0.7rem' : undefined }}>
          {visibleBarCount} / {totalBars} bars
        </span>
      ) : null}
    </div>
  )
}

/** SMA + EMA overlays for Recharts (same colors as candlestick chart). */
function MovingAverageOverlays({
  visibility,
  customEmaLinePeriod,
}: {
  visibility: MaLineVisibility
  customEmaLinePeriod: number | null
}) {
  return (
    <>
      {visibility.showSma20 ? (
        <Line
          type="monotone"
          dataKey="sma20"
          stroke={MA_LINE_COLORS.sma20}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`SMA ${MA_SMA_PERIOD}`}
        />
      ) : null}
      {visibility.showEma9 ? (
        <Line
          type="monotone"
          dataKey="ema9"
          stroke={MA_LINE_COLORS.ema9}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`EMA ${MA_EMA_FAST_PERIOD}`}
        />
      ) : null}
      {visibility.showEma21 ? (
        <Line
          type="monotone"
          dataKey="ema21"
          stroke={MA_LINE_COLORS.ema21}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`EMA ${MA_EMA_SLOW_PERIOD}`}
        />
      ) : null}
      {visibility.showCustomEma && customEmaLinePeriod != null && customEmaLinePeriod >= 2 ? (
        <Line
          type="monotone"
          dataKey="emaCustom"
          stroke={MA_LINE_COLORS.emaCustom}
          dot={false}
          strokeWidth={1.5}
          connectNulls
          name={`EMA ${customEmaLinePeriod}`}
        />
      ) : null}
    </>
  )
}

/** Matches candle chart: colored SMA/EMA key on top-right of Recharts line/bar. */
function MaChartCornerLegend({
  visibility,
  customEmaLinePeriod,
}: {
  visibility: MaLineVisibility
  customEmaLinePeriod: number | null
}) {
  const items: { key: string; label: string; color: string }[] = []
  if (visibility.showSma20) items.push({ key: 'sma', label: `SMA${MA_SMA_PERIOD}`, color: MA_LINE_COLORS.sma20 })
  if (visibility.showEma9) items.push({ key: 'e9', label: `EMA${MA_EMA_FAST_PERIOD}`, color: MA_LINE_COLORS.ema9 })
  if (visibility.showEma21) items.push({ key: 'e21', label: `EMA${MA_EMA_SLOW_PERIOD}`, color: MA_LINE_COLORS.ema21 })
  if (visibility.showCustomEma && customEmaLinePeriod != null && customEmaLinePeriod >= 2) {
    items.push({
      key: 'ecust',
      label: `EMA${customEmaLinePeriod}`,
      color: MA_LINE_COLORS.emaCustom,
    })
  }
  if (items.length === 0) return null
  return (
    <div
      className="position-absolute small text-secondary"
      style={{ right: 8, top: 4, fontSize: '0.65rem', pointerEvents: 'none', zIndex: 2 }}
    >
      {items.map((item, i) => (
        <Fragment key={item.key}>
          {i > 0 ? <span className="text-muted"> · </span> : null}
          <span style={{ color: item.color }}>{item.label}</span>
        </Fragment>
      ))}
    </div>
  )
}

type ChartPoint = ChartPointOhlc

/** Re-fetch OHLC while a chart is mounted (browser tab visible) to keep the series current. */
const CHART_LIVE_POLL_MS = 30_000

function historicalCandlesToChartPoints(data: HistoricalCandlesResponse): ChartPoint[] {
  return data.candles.map((c, idx) => ({
    idx: idx + 1,
    t: c.time,
    open: Number(c.open),
    high: Number(c.high),
    low: Number(c.low),
    close: Number(c.close),
    volume: Number(c.volume),
    ohlc: `O ${c.open}  H ${c.high}  L ${c.low}  C ${c.close}  V ${c.volume}`,
  }))
}

/** Prefer server SMA/EMA (computed after Kite warmup fetch); otherwise match in-browser. Custom EMA column is added in UI. */
function chartPointsFromHistoricalResponse(data: HistoricalCandlesResponse): ChartPointWithMa[] {
  const pts = historicalCandlesToChartPoints(data)
  const serverMa =
    data.candles.length === pts.length &&
    data.candles.some((c) => c.sma20 != null || c.ema9 != null || c.ema21 != null)
  const base: ChartPointWithMa[] = !serverMa
    ? attachMovingAverages(pts)
    : pts.map((p, i) => ({
        ...p,
        sma20: data.candles[i].sma20 != null ? Number(data.candles[i].sma20) : null,
        ema9: data.candles[i].ema9 != null ? Number(data.candles[i].ema9) : null,
        ema21: data.candles[i].ema21 != null ? Number(data.candles[i].ema21) : null,
        emaCustom: null,
      }))
  return addCustomEmaToChartPoints(base, null)
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
  maLineVisibility = DEFAULT_MA_LINE_VISIBILITY,
  customEmaLinePeriod = null,
}: {
  active?: boolean
  payload?: readonly { payload?: ChartPointWithMa }[]
  maLineVisibility?: MaLineVisibility
  customEmaLinePeriod?: number | null
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
      <div className="font-monospace mt-1">
        O {p.open} · H {p.high} · L {p.low} · C {p.close}
      </div>
      <div className="text-secondary small">Vol {p.volume}</div>
      {maLineVisibility.showSma20 && p.sma20 != null ? (
        <div className="font-monospace mt-1" style={{ color: MA_LINE_COLORS.sma20 }}>
          SMA{MA_SMA_PERIOD} {p.sma20.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showEma9 && p.ema9 != null ? (
        <div className="font-monospace" style={{ color: MA_LINE_COLORS.ema9 }}>
          EMA{MA_EMA_FAST_PERIOD} {p.ema9.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showEma21 && p.ema21 != null ? (
        <div className="font-monospace" style={{ color: MA_LINE_COLORS.ema21 }}>
          EMA{MA_EMA_SLOW_PERIOD} {p.ema21.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showCustomEma &&
      customEmaLinePeriod != null &&
      customEmaLinePeriod >= 2 &&
      p.emaCustom != null ? (
        <div className="font-monospace" style={{ color: MA_LINE_COLORS.emaCustom }}>
          EMA{customEmaLinePeriod} {p.emaCustom.toFixed(4)}
        </div>
      ) : null}
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
  maLineVisibility,
  onMaLineVisibilityChange,
  customEmaPeriod,
  onCustomEmaPeriodChange,
}: {
  idPrefix: string
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
  maLineVisibility: MaLineVisibility
  onMaLineVisibilityChange: (patch: Partial<MaLineVisibility>) => void
  customEmaPeriod: number
  onCustomEmaPeriodChange: (n: number) => void
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
          <ToggleButton
            id={`${idPrefix}-graph-candle`}
            type="radio"
            variant="outline-secondary"
            name={`${idPrefix}-chart-graph`}
            value="candlestick"
            checked={graphType === 'candlestick'}
            onChange={() => onGraphTypeChange('candlestick')}
          >
            Candles
          </ToggleButton>
        </ButtonGroup>
      </div>
      <div className="mb-3 d-flex flex-wrap align-items-center gap-2">
        <span className="small text-secondary text-uppercase me-1">Indicators</span>
        <ButtonGroup size="sm">
          <ToggleButton
            id={`${idPrefix}-ind-sma`}
            type="checkbox"
            variant={maLineVisibility.showSma20 ? 'secondary' : 'outline-secondary'}
            value="sma20"
            checked={maLineVisibility.showSma20}
            onChange={(e) => onMaLineVisibilityChange({ showSma20: e.currentTarget.checked })}
          >
            SMA {MA_SMA_PERIOD}
          </ToggleButton>
          <ToggleButton
            id={`${idPrefix}-ind-ema9`}
            type="checkbox"
            variant={maLineVisibility.showEma9 ? 'secondary' : 'outline-secondary'}
            value="ema9"
            checked={maLineVisibility.showEma9}
            onChange={(e) => onMaLineVisibilityChange({ showEma9: e.currentTarget.checked })}
          >
            EMA {MA_EMA_FAST_PERIOD}
          </ToggleButton>
          <ToggleButton
            id={`${idPrefix}-ind-ema21`}
            type="checkbox"
            variant={maLineVisibility.showEma21 ? 'secondary' : 'outline-secondary'}
            value="ema21"
            checked={maLineVisibility.showEma21}
            onChange={(e) => onMaLineVisibilityChange({ showEma21: e.currentTarget.checked })}
          >
            EMA {MA_EMA_SLOW_PERIOD}
          </ToggleButton>
          <ToggleButton
            id={`${idPrefix}-ind-emacust`}
            type="checkbox"
            variant={maLineVisibility.showCustomEma ? 'secondary' : 'outline-secondary'}
            value="emacust"
            checked={maLineVisibility.showCustomEma}
            onChange={(e) => onMaLineVisibilityChange({ showCustomEma: e.currentTarget.checked })}
          >
            Custom EMA
          </ToggleButton>
        </ButtonGroup>
        <InputGroup size="sm" style={{ width: '7.5rem' }} className="flex-nowrap">
          <InputGroup.Text className="py-0 px-2 small">Period</InputGroup.Text>
          <Form.Control
            type="number"
            className="py-0"
            min={CUSTOM_EMA_PERIOD_MIN}
            max={CUSTOM_EMA_PERIOD_MAX}
            value={customEmaPeriod}
            disabled={!maLineVisibility.showCustomEma}
            onChange={(e) => {
              const v = Math.round(Number(e.target.value))
              if (!Number.isFinite(v)) return
              onCustomEmaPeriodChange(Math.min(CUSTOM_EMA_PERIOD_MAX, Math.max(CUSTOM_EMA_PERIOD_MIN, v)))
            }}
            aria-label="Custom EMA period"
          />
        </InputGroup>
      </div>
    </>
  )
}

/** ML next-bar direction; logs last 10 predictions (local) and colors rows green/red once the next bar resolves. */
function MlNextBarBiasBar({
  instrumentToken,
  interval,
  compact,
  candleSeries,
}: {
  instrumentToken: string
  interval: ChartInterval
  compact?: boolean
  candleSeries: ChartPointWithMa[]
}) {
  const [mlPred, setMlPred] = useState<PriceDirectionApiResponse | null>(null)
  const [mlLoading, setMlLoading] = useState(false)
  const [mlError, setMlError] = useState<string | null>(null)
  const [history, setHistory] = useState<MlPredictionLogEntry[]>([])

  useEffect(() => {
    setMlPred(null)
    setMlError(null)
    setHistory(loadMlHistory(instrumentToken, interval))
  }, [instrumentToken, interval])

  useEffect(() => {
    if (candleSeries.length === 0) return
    setHistory((prev) => {
      const resolved = resolveMlHistory(prev, candleSeries)
      if (historiesEqual(prev, resolved)) return prev
      saveMlHistory(instrumentToken, interval, resolved)
      return resolved
    })
  }, [instrumentToken, interval, candleSeries])

  const fetchMlBias = useCallback(async () => {
    setMlLoading(true)
    setMlError(null)
    try {
      const { data } = await api.get<PriceDirectionApiResponse>('/predictions/price-direction', {
        params: { instrumentToken, interval },
      })
      setMlPred(data)
      const last = candleSeries.length > 0 ? candleSeries[candleSeries.length - 1] : null
      if (last) {
        setHistory((prev) => {
          const extended = appendMlPrediction(prev, data, { t: last.t, close: last.close })
          const resolved = resolveMlHistory(extended, candleSeries)
          saveMlHistory(instrumentToken, interval, resolved)
          return resolved
        })
      }
    } catch (err) {
      setMlPred(null)
      setMlError(problemDetail(err))
    } finally {
      setMlLoading(false)
    }
  }, [instrumentToken, interval, candleSeries])

  const gapClass = compact ? 'mb-1' : 'mb-2'
  const historyNewestFirst = useMemo(() => [...history].reverse(), [history])

  return (
    <>
      <div className={`d-flex flex-wrap align-items-center gap-2 ${gapClass}`}>
        <Button
          type="button"
          variant="outline-info"
          size="sm"
          className="py-0 px-2"
          disabled={mlLoading}
          onClick={() => void fetchMlBias()}
        >
          {mlLoading ? (
            <>
              <Spinner animation="border" size="sm" className="me-1" />
              ML…
            </>
          ) : (
            'ML next-bar bias'
          )}
        </Button>
        {mlPred ? (
          <span
            className={`small font-monospace fw-semibold ${
              mlPred.direction === 'up'
                ? 'text-success'
                : mlPred.direction === 'down'
                  ? 'text-danger'
                  : 'text-secondary'
            }`}
          >
            {mlPred.direction.toUpperCase()} · {mlPred.confidence}% ·{' '}
            <span className="fw-normal text-muted">{mlPred.modelId}</span>
          </span>
        ) : null}
      </div>
      {mlError ? (
        <Alert variant="warning" className={`py-1 small ${gapClass}`}>
          {mlError}
        </Alert>
      ) : null}
      {mlPred ? (
        <p
          className={`small text-muted ${gapClass}`}
          style={{ fontSize: compact ? '0.72rem' : '0.75rem' }}
        >
          {mlPred.detail}
        </p>
      ) : null}
      {history.length > 0 ? (
        <div
          className={`rounded border border-secondary overflow-hidden ${gapClass}`}
          style={{ maxHeight: compact ? '12rem' : '18rem', overflow: 'auto' }}
        >
          <div className="px-2 py-1 bg-body-secondary text-secondary border-bottom border-secondary">
            <span className="text-uppercase" style={{ fontSize: compact ? '0.62rem' : '0.65rem' }}>
              ML analysis (last {history.length}) · green / red = resolved correct / wrong · stored locally
            </span>
          </div>
          <Table
            responsive
            bordered
            size="sm"
            className="mb-0 align-middle font-monospace"
            style={{ fontSize: compact ? '0.65rem' : '0.72rem' }}
          >
            <thead className="table-light text-secondary text-nowrap">
              <tr>
                <th>#</th>
                <th>Predicted</th>
                <th>Dir</th>
                <th>Conf</th>
                <th>Outcome</th>
                <th>Ref bar</th>
                <th>Ref close</th>
                <th>Next bar</th>
                <th>Next close</th>
                <th>Model</th>
                <th className="d-none d-md-table-cell">Detail</th>
              </tr>
            </thead>
            <tbody>
              {historyNewestFirst.map((e, idx) => (
                <tr
                  key={e.id}
                  style={{
                    borderLeft: '3px solid',
                    borderLeftColor:
                      e.outcome === 'pending' ? '#6c757d' : e.outcome === 'correct' ? '#198754' : '#dc3545',
                    backgroundColor:
                      e.outcome === 'pending'
                        ? undefined
                        : e.outcome === 'correct'
                          ? 'rgba(25, 135, 84, 0.09)'
                          : 'rgba(220, 53, 69, 0.09)',
                  }}
                >
                  <td className="text-muted">{idx + 1}</td>
                  <td className="text-nowrap">{new Date(e.predictedAt).toLocaleString()}</td>
                  <td
                    className={
                      e.direction === 'up'
                        ? 'text-success fw-semibold'
                        : e.direction === 'down'
                          ? 'text-danger fw-semibold'
                          : 'text-secondary fw-semibold'
                    }
                  >
                    {e.direction.toUpperCase()}
                  </td>
                  <td>{e.confidence}%</td>
                  <td className="text-nowrap">
                    {e.outcome === 'pending' ? (
                      <span className="text-muted fst-italic">Pending</span>
                    ) : (
                      <span className={e.outcome === 'correct' ? 'text-success' : 'text-danger'}>
                        {e.outcome === 'correct' ? 'Correct' : 'Wrong'}
                      </span>
                    )}
                  </td>
                  <td className="text-nowrap">{new Date(e.refBarTime).toLocaleString()}</td>
                  <td>{e.refClose.toFixed(4)}</td>
                  <td className="text-nowrap">
                    {e.nextBarTime ? new Date(e.nextBarTime).toLocaleString() : '—'}
                  </td>
                  <td>{e.nextClose != null ? e.nextClose.toFixed(4) : '—'}</td>
                  <td className="text-truncate" style={{ maxWidth: compact ? '4.5rem' : '7rem' }} title={e.modelId}>
                    {e.modelId}
                  </td>
                  <td
                    className="d-none d-md-table-cell text-truncate text-muted"
                    style={{ maxWidth: '12rem' }}
                    title={e.detail}
                  >
                    {e.detail}
                  </td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      ) : null}
    </>
  )
}

function CompactPriceChart({
  row,
  rangePreset,
  interval,
  graphType,
  heightPx,
  maLineVisibility,
  customEmaPeriod,
  zoomVisibleBars,
  onZoomVisibleBarsChange,
}: {
  row: KiteInstrumentRow
  rangePreset: ChartRangePreset
  interval: ChartInterval
  graphType: ChartGraphType
  heightPx: number
  maLineVisibility: MaLineVisibility
  customEmaPeriod: number
  zoomVisibleBars: number | null
  onZoomVisibleBarsChange: (bars: number | null) => void
}) {
  const [series, setSeries] = useState<ChartPointWithMa[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)

  useEffect(() => {
    if (zoomVisibleBars != null && series.length > 0 && zoomVisibleBars > series.length) {
      onZoomVisibleBarsChange(null)
    }
  }, [series.length, zoomVisibleBars, onZoomVisibleBarsChange])

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
        const pts = chartPointsFromHistoricalResponse(data)
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

  const customEmaApplied = useMemo(
    () => effectiveCustomEmaPeriod(maLineVisibility, customEmaPeriod),
    [maLineVisibility, customEmaPeriod],
  )

  const seriesWithCustom = useMemo(
    () => addCustomEmaToChartPoints(series, customEmaApplied),
    [series, customEmaApplied],
  )

  const chartData = useMemo(() => sliceChartForZoom(seriesWithCustom, zoomVisibleBars), [seriesWithCustom, zoomVisibleBars])

  const rechartsYDomain = useMemo(
    () => yDomainForOhlcAndVisibleMas(chartData, maLineVisibility),
    [chartData, maLineVisibility],
  )

  const onChartZoomIn = useCallback(() => {
    onZoomVisibleBarsChange(zoomInBarCount(zoomVisibleBars, series.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, series.length])

  const onChartZoomOut = useCallback(() => {
    onZoomVisibleBarsChange(zoomOutBarCount(zoomVisibleBars, series.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, series.length])

  const onChartZoomReset = useCallback(() => onZoomVisibleBarsChange(null), [onZoomVisibleBarsChange])

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
      <MlNextBarBiasBar instrumentToken={row.instrumentToken} interval={interval} compact candleSeries={seriesWithCustom} />
      {!loading && !error && series.length > 0 ? (
        <ChartZoomControls
          idPrefix={`fav-chart-${row.instrumentToken}`}
          totalBars={series.length}
          visibleBarCount={zoomVisibleBars}
          onZoomIn={onChartZoomIn}
          onZoomOut={onChartZoomOut}
          onReset={onChartZoomReset}
          compact
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
        ) : graphType === 'candlestick' ? (
          <div className="w-100 h-100">
            <CandlestickChart
              data={chartData}
              maLineVisibility={maLineVisibility}
              customEmaPeriod={customEmaApplied}
            />
          </div>
        ) : (
          <div className="position-relative w-100 h-100">
            <ResponsiveContainer width="100%" height="100%">
              {graphType === 'line' ? (
                <LineChart data={chartData} margin={CHART_MARGINS}>
                  <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 9 }} hide />
                  <YAxis
                    stroke="#adb5bd"
                    tick={{ fontSize: 10 }}
                    domain={rechartsYDomain ?? ['auto', 'auto']}
                    width={48}
                  />
                  <Tooltip
                    content={(props) => (
                      <ChartTooltipContent
                        active={props.active}
                        payload={props.payload as readonly { payload?: ChartPointWithMa }[] | undefined}
                        maLineVisibility={maLineVisibility}
                        customEmaLinePeriod={customEmaApplied}
                      />
                    )}
                  />
                  <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} name="Close" />
                  <MovingAverageOverlays visibility={maLineVisibility} customEmaLinePeriod={customEmaApplied} />
                </LineChart>
              ) : (
                <ComposedChart data={chartData} margin={CHART_MARGINS}>
                  <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 9 }} hide />
                  <YAxis
                    stroke="#adb5bd"
                    tick={{ fontSize: 10 }}
                    domain={rechartsYDomain ?? ['auto', 'auto']}
                    width={48}
                  />
                  <Tooltip
                    content={(props) => (
                      <ChartTooltipContent
                        active={props.active}
                        payload={props.payload as readonly { payload?: ChartPointWithMa }[] | undefined}
                        maLineVisibility={maLineVisibility}
                        customEmaLinePeriod={customEmaApplied}
                      />
                    )}
                  />
                  <Bar dataKey="close" fill="#0d6efd" maxBarSize={32} radius={[2, 2, 0, 0]} name="Close" />
                  <MovingAverageOverlays visibility={maLineVisibility} customEmaLinePeriod={customEmaApplied} />
                </ComposedChart>
              )}
            </ResponsiveContainer>
            <MaChartCornerLegend visibility={maLineVisibility} customEmaLinePeriod={customEmaApplied} />
          </div>
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
  maLineVisibility,
  onMaLineVisibilityChange,
  customEmaPeriod,
  onCustomEmaPeriodChange,
  onToggleFavorite,
  chartZoomByInstrumentToken,
  onInstrumentChartZoomChange,
}: {
  favorites: KiteInstrumentRow[]
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
  maLineVisibility: MaLineVisibility
  onMaLineVisibilityChange: (patch: Partial<MaLineVisibility>) => void
  customEmaPeriod: number
  onCustomEmaPeriodChange: (n: number) => void
  onToggleFavorite: (r: KiteInstrumentRow) => void
  chartZoomByInstrumentToken: Record<string, number>
  onInstrumentChartZoomChange: (instrumentToken: string, bars: number | null) => void
}) {
  if (favorites.length === 0) return null

  return (
    <div className="mt-4">
      <h2 className="h6 mb-1">All charts</h2>
      <p className="small text-secondary mb-3">
        Historical OHLC (line, bar, or green/red candles) for every favorite below. Each chart shows{' '}
        <strong>SMA 20</strong>, <strong>EMA 9</strong>, and <strong>EMA 21</strong> on closes. Use{' '}
        <strong>ML next-bar bias</strong> per tile for the same prediction as on Browse. Interval and chart type apply to
        all tiles. Charts re-fetch about every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this browser tab is
        visible.
      </p>
      <ChartSettingsToolbar
        idPrefix="fav-all"
        rangePreset={rangePreset}
        onRangePresetChange={onRangePresetChange}
        interval={interval}
        onIntervalChange={onIntervalChange}
        graphType={graphType}
        onGraphTypeChange={onGraphTypeChange}
        maLineVisibility={maLineVisibility}
        onMaLineVisibilityChange={onMaLineVisibilityChange}
        customEmaPeriod={customEmaPeriod}
        onCustomEmaPeriodChange={onCustomEmaPeriodChange}
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
                  maLineVisibility={maLineVisibility}
                  customEmaPeriod={customEmaPeriod}
                  zoomVisibleBars={chartZoomByInstrumentToken[row.instrumentToken] ?? null}
                  onZoomVisibleBarsChange={(bars) => onInstrumentChartZoomChange(row.instrumentToken, bars)}
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
  maLineVisibility,
  onMaLineVisibilityChange,
  customEmaPeriod,
  onCustomEmaPeriodChange,
  isFavorite,
  onToggleFavorite,
  liveLastPrice,
  liveLastTick,
  zoomVisibleBars,
  onZoomVisibleBarsChange,
}: {
  selection: KiteInstrumentRow | null
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  onIntervalChange: (v: ChartInterval) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
  maLineVisibility: MaLineVisibility
  onMaLineVisibilityChange: (patch: Partial<MaLineVisibility>) => void
  customEmaPeriod: number
  onCustomEmaPeriodChange: (n: number) => void
  isFavorite: boolean
  onToggleFavorite?: () => void
  liveLastPrice?: number | null
  liveLastTick?: MarketTickBatchItem | null
  zoomVisibleBars: number | null
  onZoomVisibleBarsChange: (bars: number | null) => void
}) {
  const [series, setSeries] = useState<ChartPointWithMa[]>([])
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
    setSeries([])

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
        const pts = chartPointsFromHistoricalResponse(data)
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

  const displaySeries = useMemo(
    () => mergeLiveTickIntoOhlc(series, liveLastTick ?? null, interval, graphType),
    [series, liveLastTick, interval, graphType],
  )

  const customEmaApplied = useMemo(
    () => effectiveCustomEmaPeriod(maLineVisibility, customEmaPeriod),
    [maLineVisibility, customEmaPeriod],
  )

  const displayWithMa = useMemo(
    () => addCustomEmaToChartPoints(attachMovingAverages(displaySeries), customEmaApplied),
    [displaySeries, customEmaApplied],
  )

  useEffect(() => {
    if (zoomVisibleBars != null && displayWithMa.length > 0 && zoomVisibleBars > displayWithMa.length) {
      onZoomVisibleBarsChange(null)
    }
  }, [displayWithMa.length, zoomVisibleBars, onZoomVisibleBarsChange])

  const chartData = useMemo(() => sliceChartForZoom(displayWithMa, zoomVisibleBars), [displayWithMa, zoomVisibleBars])

  const rechartsYDomain = useMemo(
    () => yDomainForOhlcAndVisibleMas(chartData, maLineVisibility),
    [chartData, maLineVisibility],
  )

  const onChartZoomIn = useCallback(() => {
    onZoomVisibleBarsChange(zoomInBarCount(zoomVisibleBars, displayWithMa.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, displayWithMa.length])

  const onChartZoomOut = useCallback(() => {
    onZoomVisibleBarsChange(zoomOutBarCount(zoomVisibleBars, displayWithMa.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, displayWithMa.length])

  const onChartZoomReset = useCallback(() => onZoomVisibleBarsChange(null), [onZoomVisibleBarsChange])

  return (
    <Card className="border-secondary mt-4">
      <Card.Body>
        <Card.Title className="h6 mb-2">Price chart</Card.Title>
        {!selection ? (
          <p className="text-secondary small mb-0">
            Click a row in either list to plot prices from Kite (historical OHLCV). Choose <strong>Candles</strong> for
            green/red candlesticks with <strong>SMA 20</strong>, <strong>EMA 9</strong>, and <strong>EMA 21</strong>; live
            ticks update the current bar while subscribed.
          </p>
        ) : (
          <>
            <p className="small text-secondary mb-2 d-flex flex-wrap align-items-center gap-2">
              <span className="font-monospace">{selection.tradingsymbol}</span>
              <span>· {selection.exchange}</span>
              {liveLastPrice != null ? (
                <span className="font-monospace text-success">LTP {liveLastPrice}</span>
              ) : null}
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
              maLineVisibility={maLineVisibility}
              onMaLineVisibilityChange={onMaLineVisibilityChange}
              customEmaPeriod={customEmaPeriod}
              onCustomEmaPeriodChange={onCustomEmaPeriodChange}
            />
            <MlNextBarBiasBar
              instrumentToken={selection.instrumentToken}
              interval={interval}
              candleSeries={displayWithMa}
            />
            {!loading && !error && displayWithMa.length > 0 ? (
              <ChartZoomControls
                idPrefix="browse-chart-zoom"
                totalBars={displayWithMa.length}
                visibleBarCount={zoomVisibleBars}
                onZoomIn={onChartZoomIn}
                onZoomOut={onChartZoomOut}
                onReset={onChartZoomReset}
              />
            ) : null}
            <p className="small text-muted mb-2" style={{ fontSize: '0.78rem' }}>
              Historical data refreshes about every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this tab is visible.
              Charts include <strong>SMA 20</strong>, <strong>EMA 9</strong>, <strong>EMA 21</strong>, and an optional
              custom-period <strong>EMA</strong> on line, bar, and candle views. Live <strong>LTP</strong> and in-progress{' '}
              <strong>candle</strong> (Candles view) use SignalR + Kite WebSocket when a row is selected (market hours /
              session). <strong>ML next-bar bias</strong> calls{' '}
              <span className="font-monospace">/api/v1/predictions/price-direction</span> (ML.NET on the server — not
              financial advice).
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
              ) : displayWithMa.length === 0 ? (
                <p className="text-secondary small mb-0 py-5 text-center">No candles returned for this range.</p>
              ) : graphType === 'candlestick' ? (
                <CandlestickChart
                  data={chartData}
                  maLineVisibility={maLineVisibility}
                  customEmaPeriod={customEmaApplied}
                />
              ) : (
                <div className="position-relative w-100 h-100">
                  <ResponsiveContainer width="100%" height="100%">
                    {graphType === 'line' ? (
                      <LineChart data={chartData} margin={CHART_MARGINS}>
                        <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 10 }} hide />
                        <YAxis
                          stroke="#adb5bd"
                          tick={{ fontSize: 11 }}
                          domain={rechartsYDomain ?? ['auto', 'auto']}
                          width={56}
                        />
                        <Tooltip
                          content={(props) => (
                            <ChartTooltipContent
                              active={props.active}
                              payload={props.payload as readonly { payload?: ChartPointWithMa }[] | undefined}
                              maLineVisibility={maLineVisibility}
                              customEmaLinePeriod={customEmaApplied}
                            />
                          )}
                        />
                        <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} name="Close" />
                        <MovingAverageOverlays visibility={maLineVisibility} customEmaLinePeriod={customEmaApplied} />
                      </LineChart>
                    ) : (
                      <ComposedChart data={chartData} margin={CHART_MARGINS}>
                        <XAxis dataKey="idx" stroke="#adb5bd" tick={{ fontSize: 10 }} hide />
                        <YAxis
                          stroke="#adb5bd"
                          tick={{ fontSize: 11 }}
                          domain={rechartsYDomain ?? ['auto', 'auto']}
                          width={56}
                        />
                        <Tooltip
                          content={(props) => (
                            <ChartTooltipContent
                              active={props.active}
                              payload={props.payload as readonly { payload?: ChartPointWithMa }[] | undefined}
                              maLineVisibility={maLineVisibility}
                              customEmaLinePeriod={customEmaApplied}
                            />
                          )}
                        />
                        <Bar dataKey="close" fill="#0d6efd" maxBarSize={48} radius={[2, 2, 0, 0]} name="Close" />
                        <MovingAverageOverlays visibility={maLineVisibility} customEmaLinePeriod={customEmaApplied} />
                      </ComposedChart>
                    )}
                  </ResponsiveContainer>
                  <MaChartCornerLegend visibility={maLineVisibility} customEmaLinePeriod={customEmaApplied} />
                </div>
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
  const [searchParams, setSearchParams] = useSearchParams()
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

  const [mainTab, setMainTab] = useState<MainTab>(() =>
    typeof window !== 'undefined' && favoritesTabFromSearchParams(new URLSearchParams(window.location.search))
      ? 'favorites'
      : 'browse',
  )

  useEffect(() => {
    setMainTab(favoritesTabFromSearchParams(searchParams) ? 'favorites' : 'browse')
  }, [searchParams])

  const onMainTabSelect = useCallback(
    (k: string | null) => {
      if (!k) return
      const next = k as MainTab
      setMainTab(next)
      setSearchParams(
        (prev) => {
          const p = new URLSearchParams(prev)
          if (next === 'favorites') {
            p.set('tab', 'favorites')
            p.delete('fav')
          } else {
            p.delete('tab')
            p.delete('fav')
          }
          return p
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  const [provider, setProvider] = useState<string | null>(null)
  const [statusLoading, setStatusLoading] = useState(true)
  const [instruments, setInstruments] = useState<InstrumentsResponse | null>(null)
  const [instrumentsLoading, setInstrumentsLoading] = useState(false)
  const [instrumentsError, setInstrumentsError] = useState<string | null>(null)
  const [chartRow, setChartRow] = useState<KiteInstrumentRow | null>(null)
  const [chartInterval, setChartInterval] = useState<ChartInterval>('5m')
  const [chartRangePreset, setChartRangePreset] = useState<ChartRangePreset>('auto')
  const [chartGraphType, setChartGraphType] = useState<ChartGraphType>('line')
  const [customEmaPeriod, setCustomEmaPeriod] = useState(() => loadCustomEmaPrefs().period)
  const [maLineVisibility, setMaLineVisibility] = useState<MaLineVisibility>(() => {
    const p = loadCustomEmaPrefs()
    return { ...DEFAULT_MA_LINE_VISIBILITY, showCustomEma: p.show }
  })
  const patchMaLineVisibility = useCallback((patch: Partial<MaLineVisibility>) => {
    setMaLineVisibility((p) => ({ ...p, ...patch }))
  }, [])
  useEffect(() => {
    saveCustomEmaPrefs({ period: customEmaPeriod, show: maLineVisibility.showCustomEma })
  }, [customEmaPeriod, maLineVisibility.showCustomEma])

  const [chartPrefsHydrated, setChartPrefsHydrated] = useState(false)
  const [chartZoomByToken, setChartZoomByToken] = useState<Record<string, number>>({})
  const chartZoomSaveTimersRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})

  const persistInstrumentChartZoom = useCallback((instrumentToken: string, visibleBars: number | null) => {
    setChartZoomByToken((prev) => {
      const next = { ...prev }
      if (visibleBars == null) delete next[instrumentToken]
      else next[instrumentToken] = visibleBars
      return next
    })
    const timers = chartZoomSaveTimersRef.current
    const existing = timers[instrumentToken]
    if (existing) window.clearTimeout(existing)
    timers[instrumentToken] = window.setTimeout(() => {
      void api
        .put('/broker/kite/instruments/chart-zoom', { instrumentToken, visibleBars })
        .catch(() => {
          /* non-fatal */
        })
      delete timers[instrumentToken]
    }, 400)
  }, [])

  useEffect(
    () => () => {
      Object.values(chartZoomSaveTimersRef.current).forEach((tid) => window.clearTimeout(tid))
    },
    [],
  )

  const loadChartSettings = useCallback(async () => {
    try {
      const { data } = await api.get<KiteInstrumentsChartSettingsDto>('/broker/kite/instruments/chart-settings')
      setChartInterval(coerceChartInterval(data.interval))
      setChartRangePreset(coerceChartRangePreset(data.rangePreset))
      setChartGraphType(coerceChartGraphType(data.graphType))
      setChartZoomByToken(
        data.zoomByInstrumentToken && typeof data.zoomByInstrumentToken === 'object'
          ? { ...data.zoomByInstrumentToken }
          : {},
      )
    } catch {
      // keep defaults
    } finally {
      setChartPrefsHydrated(true)
    }
  }, [])

  useEffect(() => {
    void loadChartSettings()
  }, [loadChartSettings])

  useEffect(() => {
    if (!chartPrefsHydrated) return
    const t = window.setTimeout(() => {
      void api
        .put('/broker/kite/instruments/chart-settings', {
          interval: chartInterval,
          rangePreset: chartRangePreset,
          graphType: chartGraphType,
        })
        .catch(() => {
          /* non-fatal */
        })
    }, 400)
    return () => window.clearTimeout(t)
  }, [chartInterval, chartRangePreset, chartGraphType, chartPrefsHydrated])

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

  const liveMarket = useLiveMarketTick(chartRow?.instrumentToken ?? null, isZerodha && mainTab === 'browse' && !!chartRow)
  const liveLtp = liveMarket.lastPrice
  const liveLastTick = liveMarket.lastTick

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
                  server. Favorites are saved to <strong>your account on the server</strong> (not just this browser).
                  Chart range, interval, and chart style (line / bar / candles) use the <strong>same server account</strong>;
                  all charts overlay <strong>SMA 20</strong>, <strong>EMA 9</strong>, and <strong>EMA 21</strong>. Use the{' '}
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
              onSelect={(k) => k && onMainTabSelect(k)}
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
                  maLineVisibility={maLineVisibility}
                  onMaLineVisibilityChange={patchMaLineVisibility}
                  customEmaPeriod={customEmaPeriod}
                  onCustomEmaPeriodChange={setCustomEmaPeriod}
                  chartZoomByInstrumentToken={chartZoomByToken}
                  onInstrumentChartZoomChange={persistInstrumentChartZoom}
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
                maLineVisibility={maLineVisibility}
                onMaLineVisibilityChange={patchMaLineVisibility}
                customEmaPeriod={customEmaPeriod}
                onCustomEmaPeriodChange={setCustomEmaPeriod}
                isFavorite={chartRow ? favoriteKeySet.has(favoriteRowKey(chartRow)) : false}
                onToggleFavorite={
                  chartRow ? () => void toggleFavorite(chartRow) : undefined
                }
                liveLastPrice={liveLtp}
                liveLastTick={liveLastTick}
                zoomVisibleBars={
                  chartRow ? chartZoomByToken[chartRow.instrumentToken] ?? null : null
                }
                onZoomVisibleBarsChange={(bars) => {
                  if (chartRow) persistInstrumentChartZoom(chartRow.instrumentToken, bars)
                }}
              />
            ) : null}
          </Card.Body>
        </Card>
      ) : null}
    </Layout>
  )
}
