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
  Cell,
  ComposedChart,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
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
import { useChartFullscreen } from '../hooks/useChartFullscreen'
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
  historyItemsFromApi,
  loadMlHistory,
  ML_LIGHTGBM_TRIPLE_BARRIER_MODEL_ID,
  resolveMlHistory,
  saveMlHistory,
  sortByPredictedAtNewestFirst,
  type MlPredictionLogEntry,
  type MlPriceDirectionHistoryApiRow,
} from '../utils/mlPredictionHistory'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
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
  SR_LINE_COLORS,
  SR_SWING_PERIOD,
  yDomainForOhlcAndVisibleMas,
  type ChartPointWithMa,
  type MaLineVisibility,
} from '../utils/movingAverages'
import {
  attachLinearTrendToChartPoints,
  LINEAR_CLOSE_TREND_COLOR,
  yDomainForTrendRecharts,
} from '../utils/closeLinearTrend'

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
    srSupport?: number | null
    srResistance?: number | null
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
  predictionId?: string | null
  refBarTime?: string | null
  refClose?: number | null
  predictedAt?: string | null
  /** When the server persisted a row: which table it used. */
  predictionStorage?: 'classicPriceDirection' | 'lightgbmTripleBarrier' | null
}

/** GET /api/v1/predictions/price-direction/models */
interface PriceDirectionModelsApiResponse {
  defaultModelId: string
  models: { id: string; description: string }[]
}

interface KiteFavoritesResponse {
  items: KiteInstrumentRow[]
}

interface KiteInstrumentsChartSettingsDto {
  interval: string | null
  rangePreset: string | null
  graphType: string | null
  zoomByInstrumentToken?: Record<string, number> | null
  intervalByInstrumentToken?: Record<string, string> | null
  mlAutomationEnabled?: boolean
}

/** Nested shape from broker list / movers API (camelCase JSON). */
interface KiteInstrumentApiItem {
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

interface TodayTopPerformerDto {
  instrument: KiteInstrumentApiItem
  lastPrice: number
  previousClose: number
  changePercent: number
}

interface TodayTopPerformersResponse {
  items: TodayTopPerformerDto[]
  basis: string
}

interface MlAutomationRecentRow {
  id: string
  predictedAt: string
  instrumentToken: string
  tradingsymbol: string | null
  exchange: string | null
  interval: string
  refBarTime: string
  refClose: number
  direction: 'up' | 'down' | 'neutral'
  confidence: number
  outcome: 'pending' | 'correct' | 'wrong'
  nextBarTime: string | null
  nextClose: number | null
  /** Registered prediction engine id for this automation row (which model slot was invoked). */
  engineModelId: string
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

type ChartGraphType = 'line' | 'bar' | 'candlestick' | 'trend'

function coerceChartInterval(v: string | null | undefined): ChartInterval {
  if (v && (CHART_INTERVALS as readonly string[]).includes(v)) return v as ChartInterval
  return '5m'
}

function coerceChartIntervalOverrideMap(
  v: Record<string, string> | null | undefined,
): Record<string, ChartInterval> {
  if (!v || typeof v !== 'object') return {}
  const out: Record<string, ChartInterval> = {}
  for (const [token, raw] of Object.entries(v)) {
    if (!raw || !(CHART_INTERVALS as readonly string[]).includes(raw)) continue
    out[token] = raw as ChartInterval
  }
  return out
}

function coerceChartRangePreset(v: string | null | undefined): ChartRangePreset {
  if (v && (CHART_RANGE_PRESETS as readonly string[]).includes(v)) return v as ChartRangePreset
  return 'auto'
}

function coerceChartGraphType(v: string | null | undefined): ChartGraphType {
  if (v === 'bar' || v === 'line' || v === 'candlestick' || v === 'trend') return v
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

function kiteInstrumentApiToRow(i: KiteInstrumentApiItem): KiteInstrumentRow {
  return {
    instrumentToken: i.instrumentToken,
    tradingsymbol: i.tradingsymbol,
    exchange: i.exchange,
    name: i.name,
    instrumentType: i.instrumentType,
    segment: i.segment,
    expiry: i.expiry,
    strike: i.strike != null ? Number(i.strike) : null,
    lotSize: i.lotSize ?? null,
  }
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
  kiteLiveSegmentScope = 'panel',
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
  /** `panel`: one server scan for this panel's segment. `all`: F&O + MCX (e.g. favorites). */
  kiteLiveSegmentScope?: 'panel' | 'all'
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

    setLiveSearchLoading(true)
    setLiveSearchError(null)
    try {
      const segments: Array<'fno' | 'mcx'> =
        kiteLiveSegmentScope === 'all' ? ['fno', 'mcx'] : [searchSegment]
      const responses = await Promise.all(
        segments.map((segment) =>
          api.get<InstrumentSearchResponse>('/broker/kite/instruments/search', {
            params: { q, segment },
          }),
        ),
      )
      const mergedByKey = new Map<string, KiteInstrumentRow>()
      let scanTruncated = false
      for (const { data } of responses) {
        if (data.scanTruncated) scanTruncated = true
        for (const item of data.items) {
          const key = favoriteRowKey(item)
          if (!mergedByKey.has(key)) mergedByKey.set(key, item)
        }
      }
      setServerHits([...mergedByKey.values()])
      setServerScanTruncated(scanTruncated)
      setServerMode(true)
    } catch (err) {
      setLiveSearchError(problemDetail(err))
      setServerMode(false)
      setServerHits([])
      setServerScanTruncated(false)
    } finally {
      setLiveSearchLoading(false)
    }
  }, [
    search,
    loading,
    liveSearchLoading,
    searchSegment,
    kiteLiveSegmentScope,
    enableKiteLiveSearch,
  ])

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
                disabled={combinedLoading || !search.trim()}
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
                        : `Showing all ${totalFiltered.toLocaleString()} match${totalFiltered === 1 ? '' : 'es'} (${rows.length.toLocaleString()} in preview). Use Search Kite for the full file.`
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

/** Top-left scroll stack when the chart panel is browser-fullscreen. */
const CHART_FULLSCREEN_META_WRAP_CLASS = 'align-self-start text-start mb-2 flex-shrink-0 small border-bottom border-secondary pb-2'
const CHART_FULLSCREEN_META_WRAP_STYLE: { maxHeight: string; overflowY: 'auto'; maxWidth: string; WebkitOverflowScrolling: 'touch' } = {
  maxHeight: 'min(42vh, 28rem)',
  overflowY: 'auto',
  maxWidth: 'min(42rem, 100%)',
  WebkitOverflowScrolling: 'touch',
}

/** Caption strip + sticky thead + ~5 tbody rows; additional rows scroll inside the box. */
const ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT_COMPACT = 'calc(1.85rem + 2.1rem + (5 * 2rem))'
const ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT = 'calc(2rem + 2.35rem + (5 * 2.15rem))'

/** Fewer bars visible (most recent on the right); reindexed for chart axes. */
function ChartZoomControls({
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
      {refreshBtn}
      {fullscreenBtn}
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
      {visibility.showSupportResistance ? (
        <Line
          type="monotone"
          dataKey="srSupport"
          stroke={SR_LINE_COLORS.support}
          dot={false}
          strokeWidth={1.25}
          connectNulls
          strokeDasharray="4 3"
          name={`Support (${SR_SWING_PERIOD})`}
        />
      ) : null}
      {visibility.showSupportResistance ? (
        <Line
          type="monotone"
          dataKey="srResistance"
          stroke={SR_LINE_COLORS.resistance}
          dot={false}
          strokeWidth={1.25}
          connectNulls
          strokeDasharray="4 3"
          name={`Resistance (${SR_SWING_PERIOD})`}
        />
      ) : null}
    </>
  )
}

/** Matches candle chart: colored SMA/EMA key on top-right of Recharts line/bar. */
function MaChartCornerLegend({
  visibility,
  customEmaLinePeriod,
  linearTrendShown = false,
}: {
  visibility: MaLineVisibility
  customEmaLinePeriod: number | null
  linearTrendShown?: boolean
}) {
  const items: { key: string; label: string; color: string }[] = []
  if (linearTrendShown) {
    items.push({ key: 'tlr', label: 'Trend LR', color: LINEAR_CLOSE_TREND_COLOR })
  }
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
  if (visibility.showSupportResistance) {
    items.push({ key: 'srs', label: `S${SR_SWING_PERIOD}`, color: SR_LINE_COLORS.support })
    items.push({ key: 'srr', label: `R${SR_SWING_PERIOD}`, color: SR_LINE_COLORS.resistance })
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

/** Prefer server SMA/EMA/support–resistance (after Kite warmup); otherwise compute MAs in-browser. S/R is server-only when using API candles. Custom EMA column is added in UI. */
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
        srSupport: data.candles[i].srSupport != null ? Number(data.candles[i].srSupport) : null,
        srResistance: data.candles[i].srResistance != null ? Number(data.candles[i].srResistance) : null,
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
  const from = formatLocalDateTime(fromIso)
  const to = formatLocalDateTime(toIso)
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
  showLinearTrend = false,
}: {
  active?: boolean
  payload?: readonly { payload?: ChartPointWithMa & { trendLine?: number | null } }[]
  maLineVisibility?: MaLineVisibility
  customEmaLinePeriod?: number | null
  showLinearTrend?: boolean
}) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload
  if (!p) return null
  return (
    <div
      className="rounded border border-secondary p-2 small"
      style={{ background: '#212529', color: '#f8f9fa' }}
    >
      <div>{formatLocalDateTime(p.t)}</div>
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
      {maLineVisibility.showSupportResistance && p.srSupport != null ? (
        <div className="font-monospace mt-1" style={{ color: SR_LINE_COLORS.support }}>
          Sup{SR_SWING_PERIOD} {p.srSupport.toFixed(4)}
        </div>
      ) : null}
      {maLineVisibility.showSupportResistance && p.srResistance != null ? (
        <div className="font-monospace" style={{ color: SR_LINE_COLORS.resistance }}>
          Res{SR_SWING_PERIOD} {p.srResistance.toFixed(4)}
        </div>
      ) : null}
      {showLinearTrend && p.trendLine != null && Number.isFinite(p.trendLine) ? (
        <div className="font-monospace mt-1" style={{ color: LINEAR_CLOSE_TREND_COLOR }}>
          Trend LR {Number(p.trendLine).toFixed(4)}
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
                    ? 'Last calendar month'
                    : 'History window; candle range captions use your local timezone'
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
          <ToggleButton
            id={`${idPrefix}-graph-trend`}
            type="radio"
            variant="outline-secondary"
            name={`${idPrefix}-chart-graph`}
            value="trend"
            checked={graphType === 'trend'}
            title="Close + linear regression trend (least squares)"
            onChange={() => onGraphTypeChange('trend')}
          >
            Trend
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
          <ToggleButton
            id={`${idPrefix}-ind-sr`}
            type="checkbox"
            variant={maLineVisibility.showSupportResistance ? 'secondary' : 'outline-secondary'}
            value="sr"
            checked={maLineVisibility.showSupportResistance}
            onChange={(e) => onMaLineVisibilityChange({ showSupportResistance: e.currentTarget.checked })}
          >
            S/R {SR_SWING_PERIOD}
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

/** Correct / wrong / pending counts for ML prediction history (pie chart). */
type MlOutcomeCounts = { correct: number; wrong: number; pending: number }

function MlOutcomePieChart({ counts, height }: { counts: MlOutcomeCounts; height: number }) {
  const { correct, wrong, pending } = counts
  const pieData = useMemo(() => {
    const rows: { name: string; value: number; fill: string }[] = []
    if (correct > 0) rows.push({ name: 'Correct', value: correct, fill: '#198754' })
    if (wrong > 0) rows.push({ name: 'Wrong', value: wrong, fill: '#dc3545' })
    if (pending > 0) rows.push({ name: 'Pending', value: pending, fill: '#6c757d' })
    return rows
  }, [correct, wrong, pending])

  const total = correct + wrong + pending
  if (total === 0) {
    return (
      <div
        className="d-flex align-items-center justify-content-center border border-secondary rounded bg-body-secondary text-secondary small"
        style={{ height, maxWidth: 360 }}
      >
        No predictions to chart
      </div>
    )
  }

  return (
    <div style={{ width: '100%', maxWidth: 400, height }}>
      <ResponsiveContainer width="100%" height="100%">
        <PieChart>
          <Pie
            data={pieData}
            dataKey="value"
            nameKey="name"
            cx="50%"
            cy="50%"
            outerRadius={height > 240 ? 110 : 85}
            paddingAngle={pieData.length > 1 ? 1.5 : 0}
            label={({ name, percent }) => `${name} ${(percent * 100).toFixed(0)}%`}
          >
            {pieData.map((d) => (
              <Cell key={d.name} fill={d.fill} />
            ))}
          </Pie>
          <Tooltip formatter={(value: number) => [`${value} row(s)`, 'Count']} />
          <Legend />
        </PieChart>
      </ResponsiveContainer>
    </div>
  )
}

function MlPredictionHistoryTableBody({
  rows,
  compact,
}: {
  rows: MlPredictionLogEntry[]
  compact?: boolean
}) {
  return (
    <Table
      responsive
      bordered
      size="sm"
      className="mb-0 align-middle font-monospace"
      style={{ fontSize: compact ? '0.65rem' : '0.72rem' }}
    >
      <thead
        className="table-light text-secondary text-nowrap"
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 2,
          boxShadow: 'inset 0 -1px 0 var(--bs-border-color)',
        }}
      >
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
        {rows.map((e, idx) => (
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
            <td className="text-nowrap">{formatLocalDateTime(e.predictedAt)}</td>
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
            <td className="text-nowrap">{formatLocalDateTime(e.refBarTime)}</td>
            <td>{e.refClose.toFixed(4)}</td>
            <td className="text-nowrap">{e.nextBarTime ? formatLocalDateTime(e.nextBarTime) : '—'}</td>
            <td>{e.nextClose != null ? e.nextClose.toFixed(4) : '—'}</td>
            <td className="text-truncate" style={{ maxWidth: compact ? '4.5rem' : '7rem' }} title={e.modelId}>
              {e.modelId}
            </td>
            <td className="d-none d-md-table-cell text-truncate text-muted" style={{ maxWidth: '12rem' }} title={e.detail}>
              {e.detail}
            </td>
          </tr>
        ))}
      </tbody>
    </Table>
  )
}

/** ML next-bar direction; classic vs LightGBM rows use separate server tables and history endpoints. */
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
  const [priceModels, setPriceModels] = useState<PriceDirectionModelsApiResponse | null>(null)
  const [selectedPriceModelId, setSelectedPriceModelId] = useState<string>('')
  const [history, setHistory] = useState<MlPredictionLogEntry[]>([])
  const [lightGbmHistory, setLightGbmHistory] = useState<MlPredictionLogEntry[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)
  const historySourceRef = useRef<'api' | 'local'>('api')
  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()

  const storesPredictionsInLightGbm = useMemo(() => {
    if (selectedPriceModelId)
      return selectedPriceModelId === ML_LIGHTGBM_TRIPLE_BARRIER_MODEL_ID
    return priceModels?.defaultModelId === ML_LIGHTGBM_TRIPLE_BARRIER_MODEL_ID
  }, [selectedPriceModelId, priceModels?.defaultModelId])

  const outcomeCounts = useMemo(() => {
    let correct = 0
    let wrong = 0
    let pending = 0
    for (const e of history) {
      if (e.outcome === 'correct') correct++
      else if (e.outcome === 'wrong') wrong++
      else pending++
    }
    return { correct, wrong, pending }
  }, [history])

  const lightGbmOutcomeCounts = useMemo(() => {
    let correct = 0
    let wrong = 0
    let pending = 0
    for (const e of lightGbmHistory) {
      if (e.outcome === 'correct') correct++
      else if (e.outcome === 'wrong') wrong++
      else pending++
    }
    return { correct, wrong, pending }
  }, [lightGbmHistory])

  const reloadHistory = useCallback(async () => {
    setHistoryLoading(true)
    try {
      const [classicRes, lgbmRes] = await Promise.allSettled([
        api.get<MlPriceDirectionHistoryApiRow[]>('/predictions/price-direction/history', {
          params: { instrumentToken, interval, take: 2000 },
        }),
        api.get<MlPriceDirectionHistoryApiRow[]>('/predictions/price-direction/lightgbm-triple-barrier/history', {
          params: { instrumentToken, interval, take: 2000 },
        }),
      ])

      if (classicRes.status === 'fulfilled') {
        historySourceRef.current = 'api'
        setHistory(historyItemsFromApi(classicRes.value.data))
      } else {
        historySourceRef.current = 'local'
        setHistory(loadMlHistory(instrumentToken, interval))
      }

      if (lgbmRes.status === 'fulfilled') {
        setLightGbmHistory(historyItemsFromApi(lgbmRes.value.data))
      } else {
        setLightGbmHistory([])
      }
    } finally {
      setHistoryLoading(false)
    }
  }, [instrumentToken, interval])

  useEffect(() => {
    const ac = new AbortController()
    void (async () => {
      try {
        const { data } = await api.get<PriceDirectionModelsApiResponse>('/predictions/price-direction/models', {
          signal: ac.signal,
        })
        if (!ac.signal.aborted) setPriceModels(data)
      } catch {
        if (!ac.signal.aborted) setPriceModels(null)
      }
    })()
    return () => ac.abort()
  }, [])

  useEffect(() => {
    setMlPred(null)
    setMlError(null)
    void reloadHistory()
  }, [reloadHistory])

  useEffect(() => {
    setMlPred(null)
    setMlError(null)
  }, [selectedPriceModelId])

  useEffect(() => {
    if (candleSeries.length === 0) return
    setHistory((prev) => {
      const resolved = resolveMlHistory(prev, candleSeries)
      for (const r of resolved) {
        const p = prev.find((x) => x.id === r.id)
        if (
          p &&
          p.outcome === 'pending' &&
          r.outcome !== 'pending' &&
          r.serverBacked &&
          r.nextBarTime &&
          r.nextClose != null
        ) {
          void api
            .patch(`/predictions/price-direction/${r.id}/resolve`, {
              nextBarTime: r.nextBarTime,
              nextClose: r.nextClose,
            })
            .catch(() => {})
        }
      }
      if (historiesEqual(prev, resolved)) return prev
      if (historySourceRef.current === 'local') {
        saveMlHistory(instrumentToken, interval, resolved)
      }
      return resolved
    })
  }, [instrumentToken, interval, candleSeries])

  useEffect(() => {
    if (candleSeries.length === 0) return
    setLightGbmHistory((prev) => {
      const resolved = resolveMlHistory(prev, candleSeries)
      for (const r of resolved) {
        const p = prev.find((x) => x.id === r.id)
        if (
          p &&
          p.outcome === 'pending' &&
          r.outcome !== 'pending' &&
          r.serverBacked &&
          r.nextBarTime &&
          r.nextClose != null
        ) {
          void api
            .patch(`/predictions/price-direction/${r.id}/resolve`, {
              nextBarTime: r.nextBarTime,
              nextClose: r.nextClose,
            })
            .catch(() => {})
        }
      }
      if (historiesEqual(prev, resolved)) return prev
      return resolved
    })
  }, [instrumentToken, interval, candleSeries])

  const fetchMlBias = useCallback(async () => {
    setMlLoading(true)
    setMlError(null)
    try {
      const { data } = await api.get<PriceDirectionApiResponse>('/predictions/price-direction', {
        params: {
          instrumentToken,
          interval,
          ...(selectedPriceModelId ? { model: selectedPriceModelId } : {}),
        },
      })
      setMlPred(data)
      const last = candleSeries.length > 0 ? candleSeries[candleSeries.length - 1] : null
      const refBar =
        data.predictionId && data.refBarTime != null && data.refClose != null
          ? { t: new Date(data.refBarTime).toISOString(), close: data.refClose }
          : last
            ? { t: last.t, close: last.close }
            : null
      if (!refBar) return

      const appendAndResolve = (prev: MlPredictionLogEntry[]) => {
        const extended = appendMlPrediction(prev, data, refBar, {
          serverId: data.predictionId ?? undefined,
          predictedAt: data.predictedAt ?? undefined,
        })
        return resolveMlHistory(extended, candleSeries)
      }

      const useLightGbm =
        data.predictionStorage === 'lightgbmTripleBarrier' ||
        (data.predictionStorage !== 'classicPriceDirection' && storesPredictionsInLightGbm)

      if (useLightGbm) {
        setLightGbmHistory((prev) => {
          const resolved = appendAndResolve(prev)
          if (historiesEqual(prev, resolved)) return prev
          return resolved
        })
      } else {
        setHistory((prev) => {
          const resolved = appendAndResolve(prev)
          if (historySourceRef.current === 'local') {
            saveMlHistory(instrumentToken, interval, resolved)
          }
          if (historiesEqual(prev, resolved)) return prev
          return resolved
        })
      }
      if (data.predictionId) {
        historySourceRef.current = 'api'
      }
    } catch (err) {
      setMlPred(null)
      setMlError(problemDetail(err))
    } finally {
      setMlLoading(false)
    }
  }, [instrumentToken, interval, candleSeries, selectedPriceModelId, storesPredictionsInLightGbm])

  const gapClass = compact ? 'mb-1' : 'mb-2'
  const mlHistoryTableRows = useMemo(() => sortByPredictedAtNewestFirst(history), [history])
  const mlLightGbmHistoryTableRows = useMemo(
    () => sortByPredictedAtNewestFirst(lightGbmHistory),
    [lightGbmHistory],
  )

  return (
    <div
      ref={panelRef}
      className={fullscreenActive ? 'd-flex flex-column' : undefined}
      style={
        fullscreenActive
          ? {
              minHeight: '100vh',
              height: '100%',
              background: 'var(--bs-body-bg)',
              padding: '0.5rem',
            }
          : undefined
      }
    >
      <div className={`d-flex flex-wrap align-items-center gap-2 ${fullscreenActive ? 'mb-2 flex-shrink-0' : gapClass}`}>
        {priceModels && priceModels.models.length > 0 ? (
          <Form.Select
            size="sm"
            className="py-0 w-auto"
            style={{ maxWidth: '14rem', fontSize: compact ? '0.72rem' : '0.8rem' }}
            value={selectedPriceModelId}
            aria-label="Price direction model"
            title={(() => {
              const m = priceModels.models.find((x) => x.id === selectedPriceModelId)
              return m?.description ?? `Server default (${priceModels.defaultModelId})`
            })()}
            onChange={(e) => setSelectedPriceModelId(e.target.value)}
          >
            <option value="">{`Default (${priceModels.defaultModelId})`}</option>
            {priceModels.models.map((m) => (
              <option key={m.id} value={m.id}>
                {m.id}
              </option>
            ))}
          </Form.Select>
        ) : null}
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
        <Button
          type="button"
          variant="outline-secondary"
          size="sm"
          className="py-0 px-2"
          disabled={historyLoading}
          onClick={() => void reloadHistory()}
          title="Reload prediction history from server"
          aria-label="Refresh prediction history"
        >
          {historyLoading ? (
            <>
              <Spinner animation="border" size="sm" className="me-1" role="status" />
              …
            </>
          ) : (
            '↻ Predictions'
          )}
        </Button>
        {history.length > 0 || lightGbmHistory.length > 0 ? (
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            className="py-0 px-2"
            onClick={() => void toggleFullscreen()}
            title={fullscreenActive ? 'Exit full screen' : 'Full screen predictions and chart'}
            aria-label={fullscreenActive ? 'Exit full screen' : 'Full screen predictions'}
          >
            {fullscreenActive ? (compact ? 'Exit' : 'Exit full screen') : compact ? 'Full' : 'Full predictions'}
          </Button>
        ) : null}
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
          className={`small text-muted ${fullscreenActive ? 'mb-2' : gapClass}`}
          style={{ fontSize: compact ? '0.72rem' : '0.75rem' }}
        >
          {mlPred.detail}
        </p>
      ) : null}
      {fullscreenActive && (history.length > 0 || lightGbmHistory.length > 0) ? (
        <div className="d-flex flex-column flex-md-row flex-wrap gap-3 mb-3 flex-shrink-0 align-items-md-start">
          {history.length > 0 ? (
            <div>
              <div className="small text-muted text-uppercase mb-1" style={{ fontSize: compact ? '0.62rem' : '0.65rem' }}>
                Classic models
              </div>
              <MlOutcomePieChart counts={outcomeCounts} height={compact ? 240 : 280} />
            </div>
          ) : null}
          {lightGbmHistory.length > 0 ? (
            <div>
              <div className="small text-muted text-uppercase mb-1" style={{ fontSize: compact ? '0.62rem' : '0.65rem' }}>
                LightGBM triple-barrier
              </div>
              <MlOutcomePieChart counts={lightGbmOutcomeCounts} height={compact ? 240 : 280} />
            </div>
          ) : null}
        </div>
      ) : null}
      {history.length > 0 ? (
        <div
          className={`rounded border border-secondary ${fullscreenActive ? 'mb-0 flex-grow-1 d-flex flex-column' : gapClass}`}
          style={{
            maxHeight: fullscreenActive
              ? undefined
              : compact
                ? ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT_COMPACT
                : ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT,
            flex: fullscreenActive ? '1 1 auto' : undefined,
            minHeight: fullscreenActive ? 0 : undefined,
            overflow: 'auto',
            WebkitOverflowScrolling: 'touch',
          }}
        >
          <div className="px-2 py-1 bg-body-secondary text-secondary border-bottom border-secondary flex-shrink-0">
            <span className="text-uppercase" style={{ fontSize: compact ? '0.62rem' : '0.65rem' }}>
              ML history — classic ({history.length})
            </span>
          </div>
          <MlPredictionHistoryTableBody rows={mlHistoryTableRows} compact={compact} />
        </div>
      ) : null}
      {lightGbmHistory.length > 0 ? (
        <div
          className={`rounded border border-secondary ${fullscreenActive ? 'mb-0 flex-grow-1 d-flex flex-column' : gapClass}`}
          style={{
            maxHeight: fullscreenActive
              ? undefined
              : compact
                ? ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT_COMPACT
                : ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT,
            flex: fullscreenActive ? '1 1 auto' : undefined,
            minHeight: fullscreenActive ? 0 : undefined,
            overflow: 'auto',
            WebkitOverflowScrolling: 'touch',
          }}
        >
          <div className="px-2 py-1 bg-body-secondary text-secondary border-bottom border-secondary flex-shrink-0">
            <span className="text-uppercase" style={{ fontSize: compact ? '0.62rem' : '0.65rem' }}>
              ML history — LightGBM triple-barrier ({lightGbmHistory.length})
            </span>
          </div>
          <MlPredictionHistoryTableBody rows={mlLightGbmHistoryTableRows} compact={compact} />
        </div>
      ) : null}
    </div>
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
  const [chartRefreshTick, setChartRefreshTick] = useState(0)

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
  }, [row.instrumentToken, interval, rangePreset, chartRefreshTick])

  const customEmaApplied = useMemo(
    () => effectiveCustomEmaPeriod(maLineVisibility, customEmaPeriod),
    [maLineVisibility, customEmaPeriod],
  )

  const seriesWithCustom = useMemo(
    () => addCustomEmaToChartPoints(series, customEmaApplied),
    [series, customEmaApplied],
  )

  const chartData = useMemo(() => sliceChartForZoom(seriesWithCustom, zoomVisibleBars), [seriesWithCustom, zoomVisibleBars])

  const chartDataWithTrend = useMemo(
    () => (graphType === 'trend' ? attachLinearTrendToChartPoints(chartData) : null),
    [graphType, chartData],
  )

  const rechartsData = chartDataWithTrend ?? chartData

  const rechartsYDomain = useMemo(() => {
    if (graphType === 'trend' && chartDataWithTrend && chartDataWithTrend.length > 0) {
      return yDomainForTrendRecharts(chartDataWithTrend, maLineVisibility)
    }
    return yDomainForOhlcAndVisibleMas(chartData, maLineVisibility)
  }, [graphType, chartData, chartDataWithTrend, maLineVisibility])

  const onChartZoomIn = useCallback(() => {
    onZoomVisibleBarsChange(zoomInBarCount(zoomVisibleBars, series.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, series.length])

  const onChartZoomOut = useCallback(() => {
    onZoomVisibleBarsChange(zoomOutBarCount(zoomVisibleBars, series.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, series.length])

  const onChartZoomReset = useCallback(() => onZoomVisibleBarsChange(null), [onZoomVisibleBarsChange])

  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()

  const compactHasChart = !loading && !error && series.length > 0
  const compactMetaOutside = !fullscreenActive || !compactHasChart

  return (
    <>
      {!compactHasChart ? (
        <div className="d-flex justify-content-end mb-1">
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            className="py-0 px-2"
            disabled={loading}
            onClick={() => setChartRefreshTick((n) => n + 1)}
            title="Reload candles from server"
            aria-label="Refresh chart data"
          >
            {loading ? (
              <>
                <Spinner animation="border" size="sm" className="me-1" role="status" />
                Refresh…
              </>
            ) : (
              '↻ Refresh chart'
            )}
          </Button>
        </div>
      ) : null}
      {compactMetaOutside ? (
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
        </>
      ) : null}
      {compactHasChart ? (
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
              {candleRange && !loading && !error ? (
                <HistoricalRangeCaption
                  compact
                  candleInterval={candleRange.interval}
                  fromIso={candleRange.from}
                  toIso={candleRange.to}
                />
              ) : null}
              <MlNextBarBiasBar instrumentToken={row.instrumentToken} interval={interval} compact candleSeries={seriesWithCustom} />
            </div>
          ) : null}
          <ChartZoomControls
            idPrefix={`fav-chart-${row.instrumentToken}`}
            totalBars={series.length}
            visibleBarCount={zoomVisibleBars}
            onZoomIn={onChartZoomIn}
            onZoomOut={onChartZoomOut}
            onReset={onChartZoomReset}
            compact
            onToggleFullscreen={toggleFullscreen}
            fullscreenActive={fullscreenActive}
            onRefreshChart={() => setChartRefreshTick((n) => n + 1)}
            chartRefreshing={loading}
          />
          <div
            className={fullscreenActive ? 'flex-grow-1 w-100' : 'w-100'}
            style={{
              height: fullscreenActive ? '100%' : heightPx,
              flex: fullscreenActive ? '1 1 auto' : undefined,
              minHeight: fullscreenActive ? 0 : undefined,
            }}
          >
            {graphType === 'candlestick' ? (
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
                  {graphType === 'line' || graphType === 'trend' ? (
                    <LineChart data={rechartsData} margin={CHART_MARGINS}>
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
                            payload={
                              props.payload as readonly {
                                payload?: ChartPointWithMa & { trendLine?: number | null }
                              }[] | undefined
                            }
                            maLineVisibility={maLineVisibility}
                            customEmaLinePeriod={customEmaApplied}
                            showLinearTrend={graphType === 'trend'}
                          />
                        )}
                      />
                      <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} name="Close" />
                      {graphType === 'trend' ? (
                        <Line
                          type="monotone"
                          dataKey="trendLine"
                          stroke={LINEAR_CLOSE_TREND_COLOR}
                          dot={false}
                          strokeWidth={2}
                          strokeDasharray="6 4"
                          connectNulls
                          name="Trend (linear regression)"
                        />
                      ) : null}
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
                <MaChartCornerLegend
                  visibility={maLineVisibility}
                  customEmaLinePeriod={customEmaApplied}
                  linearTrendShown={graphType === 'trend'}
                />
              </div>
            )}
          </div>
        </div>
      ) : (
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
          ) : (
            <p className="text-secondary small mb-0 text-center py-4">No candles.</p>
          )}
        </div>
      )}
    </>
  )
}

function FavoritesChartsGrid({
  favorites,
  rangePreset,
  onRangePresetChange,
  defaultInterval,
  onDefaultIntervalChange,
  chartIntervalByInstrumentToken,
  onInstrumentIntervalChange,
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
  defaultInterval: ChartInterval
  onDefaultIntervalChange: (v: ChartInterval) => void
  chartIntervalByInstrumentToken: Record<string, ChartInterval>
  onInstrumentIntervalChange: (instrumentToken: string, interval: ChartInterval | null) => void
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
      <h2 className="h6 mb-3">All charts</h2>
      <ChartSettingsToolbar
        idPrefix="fav-all"
        rangePreset={rangePreset}
        onRangePresetChange={onRangePresetChange}
        interval={defaultInterval}
        onIntervalChange={onDefaultIntervalChange}
        graphType={graphType}
        onGraphTypeChange={onGraphTypeChange}
        maLineVisibility={maLineVisibility}
        onMaLineVisibilityChange={onMaLineVisibilityChange}
        customEmaPeriod={customEmaPeriod}
        onCustomEmaPeriodChange={onCustomEmaPeriodChange}
      />
      <p className="small text-secondary mb-3" style={{ maxWidth: '48rem' }}>
        <strong>Default interval</strong> applies to every favorite chart unless you pick a symbol-specific interval on
        that card. Server <strong>Auto ML</strong> usually runs on <strong>1m</strong> candles (see{' '}
        <span className="font-monospace text-body-secondary">FavoriteMlAutomation:PredictionIntervalOverride</span>) so it
        is not slowed by 3m/5m bar closes; it can be configured to follow these chart intervals instead.
      </p>
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
                <Form.Group className="mb-2" controlId={`fav-iv-${row.instrumentToken}`}>
                  <Form.Label className="small text-secondary text-uppercase mb-1">Candles for this symbol</Form.Label>
                  <Form.Select
                    size="sm"
                    value={chartIntervalByInstrumentToken[row.instrumentToken] ?? ''}
                    onChange={(e) => {
                      const v = e.target.value
                      onInstrumentIntervalChange(row.instrumentToken, v === '' ? null : (v as ChartInterval))
                    }}
                    aria-label={`Chart interval for ${row.tradingsymbol}`}
                  >
                    <option value="">Default ({defaultInterval})</option>
                    {CHART_INTERVALS.map((iv) => (
                      <option key={iv} value={iv}>
                        {iv}
                      </option>
                    ))}
                  </Form.Select>
                </Form.Group>
                <CompactPriceChart
                  row={row}
                  rangePreset={rangePreset}
                  interval={chartIntervalByInstrumentToken[row.instrumentToken] ?? defaultInterval}
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
  const [chartRefreshTick, setChartRefreshTick] = useState(0)
  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()

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
  }, [selection, interval, rangePreset, chartRefreshTick])

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

  const chartDataWithTrend = useMemo(
    () => (graphType === 'trend' ? attachLinearTrendToChartPoints(chartData) : null),
    [graphType, chartData],
  )

  const rechartsData = chartDataWithTrend ?? chartData

  const rechartsYDomain = useMemo(() => {
    if (graphType === 'trend' && chartDataWithTrend && chartDataWithTrend.length > 0) {
      return yDomainForTrendRecharts(chartDataWithTrend, maLineVisibility)
    }
    return yDomainForOhlcAndVisibleMas(chartData, maLineVisibility)
  }, [graphType, chartData, chartDataWithTrend, maLineVisibility])

  const onChartZoomIn = useCallback(() => {
    onZoomVisibleBarsChange(zoomInBarCount(zoomVisibleBars, displayWithMa.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, displayWithMa.length])

  const onChartZoomOut = useCallback(() => {
    onZoomVisibleBarsChange(zoomOutBarCount(zoomVisibleBars, displayWithMa.length))
  }, [onZoomVisibleBarsChange, zoomVisibleBars, displayWithMa.length])

  const onChartZoomReset = useCallback(() => onZoomVisibleBarsChange(null), [onZoomVisibleBarsChange])

  const browseHasChartData = !loading && !error && displayWithMa.length > 0
  const browseDetailMetaInFullscreen = fullscreenActive && browseHasChartData

  const browseDetailMeta =
    selection == null ? null : (
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
      </>
    )

  return (
    <Card className="border-secondary mt-4">
      <Card.Body>
        <Card.Title className="h6 mb-2">Price chart</Card.Title>
        {!selection ? (
          <p className="text-secondary small mb-0">
            Choose <strong>Candles</strong> for OHLC candles, <strong>Trend</strong> for close plus a dashed linear regression
            line over the visible window, or line/bar views. Charts support <strong>SMA 20</strong>,{' '}
            <strong>EMA 9</strong>, <strong>EMA 21</strong>; live ticks update the current bar in <strong>Candles</strong>{' '}
            while subscribed.
          </p>
        ) : (
          <>
            {!browseDetailMetaInFullscreen ? browseDetailMeta : null}
            {selection && !browseHasChartData ? (
              <div className="d-flex justify-content-end mb-2">
                <Button
                  type="button"
                  variant="outline-secondary"
                  size="sm"
                  disabled={loading}
                  onClick={() => setChartRefreshTick((n) => n + 1)}
                  title="Reload candles from server"
                  aria-label="Refresh chart data"
                >
                  {loading ? (
                    <>
                      <Spinner animation="border" size="sm" className="me-1" role="status" />
                      Refresh…
                    </>
                  ) : (
                    '↻ Refresh chart'
                  )}
                </Button>
              </div>
            ) : null}
            {browseHasChartData ? (
              <div
                ref={panelRef}
                className={fullscreenActive ? 'd-flex flex-column mb-2' : undefined}
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
                    {browseDetailMeta}
                  </div>
                ) : null}
                <ChartZoomControls
                  idPrefix="browse-chart-zoom"
                  totalBars={displayWithMa.length}
                  visibleBarCount={zoomVisibleBars}
                  onZoomIn={onChartZoomIn}
                  onZoomOut={onChartZoomOut}
                  onReset={onChartZoomReset}
                  onToggleFullscreen={toggleFullscreen}
                  fullscreenActive={fullscreenActive}
                  onRefreshChart={() => setChartRefreshTick((n) => n + 1)}
                  chartRefreshing={loading}
                />
                <div
                  className={fullscreenActive ? 'flex-grow-1 w-100' : undefined}
                  style={{
                    height: fullscreenActive ? '100%' : '18rem',
                    flex: fullscreenActive ? '1 1 auto' : undefined,
                    minHeight: fullscreenActive ? 0 : undefined,
                  }}
                >
                  {graphType === 'candlestick' ? (
                    <CandlestickChart
                      data={chartData}
                      maLineVisibility={maLineVisibility}
                      customEmaPeriod={customEmaApplied}
                    />
                  ) : (
                    <div className="position-relative w-100 h-100">
                        <ResponsiveContainer width="100%" height="100%">
                          {graphType === 'line' || graphType === 'trend' ? (
                            <LineChart data={rechartsData} margin={CHART_MARGINS}>
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
                                    payload={
                                      props.payload as readonly {
                                        payload?: ChartPointWithMa & { trendLine?: number | null }
                                      }[] | undefined
                                    }
                                    maLineVisibility={maLineVisibility}
                                    customEmaLinePeriod={customEmaApplied}
                                    showLinearTrend={graphType === 'trend'}
                                  />
                                )}
                              />
                              <Line type="monotone" dataKey="close" stroke="#0d6efd" dot={false} strokeWidth={2} name="Close" />
                              {graphType === 'trend' ? (
                                <Line
                                  type="monotone"
                                  dataKey="trendLine"
                                  stroke={LINEAR_CLOSE_TREND_COLOR}
                                  dot={false}
                                  strokeWidth={2}
                                  strokeDasharray="6 4"
                                  connectNulls
                                  name="Trend (linear regression)"
                                />
                              ) : null}
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
                                    payload={
                                      props.payload as readonly { payload?: ChartPointWithMa }[] | undefined
                                    }
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
                        <MaChartCornerLegend
                          visibility={maLineVisibility}
                          customEmaLinePeriod={customEmaApplied}
                          linearTrendShown={graphType === 'trend'}
                        />
                    </div>
                  )}
                </div>
              </div>
            ) : null}
            <p className="small text-muted mb-2" style={{ fontSize: '0.78rem' }}>
              Historical data refreshes about every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this tab is visible.
              Charts include <strong>SMA 20</strong>, <strong>EMA 9</strong>, <strong>EMA 21</strong>, optional S/R bands,
              and an optional custom-period <strong>EMA</strong> on line, bar, candles, and <strong>Trend</strong>{' '}
              (linear regression on close over the zoomed bars). Live <strong>LTP</strong> and in-progress{' '}
              <strong>candle</strong> (Candles view) use SignalR + Kite WebSocket when a row is selected (market hours /
              session). <strong>ML next-bar bias</strong> calls{' '}
              <span className="font-monospace">/api/v1/predictions/price-direction</span> with an optional{' '}
              <span className="font-monospace">model</span> query (see{' '}
              <span className="font-monospace">/predictions/price-direction/models</span>); not financial advice.
            </p>
            {error ? (
              <Alert variant="danger" className="py-2 small mb-2">
                {error}
              </Alert>
            ) : null}
            {loading || displayWithMa.length === 0 ? (
              <div style={{ height: '18rem' }}>
                {loading ? (
                  <div className="d-flex align-items-center gap-2 text-secondary small py-5 justify-content-center">
                    <Spinner animation="border" size="sm" role="status" />
                    Loading candles…
                  </div>
                ) : (
                  <p className="text-secondary small mb-0 py-5 text-center">No candles returned for this range.</p>
                )}
              </div>
            ) : null}
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
          setChartIntervalByToken((prev) => {
            const next = { ...prev }
            delete next[r.instrumentToken]
            return next
          })
          void api
            .put('/broker/kite/instruments/chart-interval', {
              instrumentToken: r.instrumentToken,
              interval: null,
            })
            .catch(() => {})
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
  const [todayTopPerformers, setTodayTopPerformers] = useState<TodayTopPerformerDto[]>([])
  const [todayTopBasis, setTodayTopBasis] = useState('')
  const [todayTopLoading, setTodayTopLoading] = useState(false)
  const [todayTopError, setTodayTopError] = useState<string | null>(null)
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
  const [favoriteMlAutomationEnabled, setFavoriteMlAutomationEnabled] = useState(false)
  const [mlAutomationSaving, setMlAutomationSaving] = useState(false)
  const [mlAutomationError, setMlAutomationError] = useState<string | null>(null)
  const [automationRecent, setAutomationRecent] = useState<MlAutomationRecentRow[]>([])
  const [automationRecentLoading, setAutomationRecentLoading] = useState(false)
  const automationRecentSorted = useMemo(
    () => sortByPredictedAtNewestFirst(automationRecent),
    [automationRecent],
  )
  const [chartZoomByToken, setChartZoomByToken] = useState<Record<string, number>>({})
  const [chartIntervalByToken, setChartIntervalByToken] = useState<Record<string, ChartInterval>>({})
  const chartZoomSaveTimersRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})
  const chartIntervalSaveTimersRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})

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

  const persistInstrumentChartInterval = useCallback((instrumentToken: string, interval: ChartInterval | null) => {
    setChartIntervalByToken((prev) => {
      const next = { ...prev }
      if (interval == null) delete next[instrumentToken]
      else next[instrumentToken] = interval
      return next
    })
    const timers = chartIntervalSaveTimersRef.current
    const existing = timers[instrumentToken]
    if (existing) window.clearTimeout(existing)
    timers[instrumentToken] = window.setTimeout(() => {
      void api
        .put('/broker/kite/instruments/chart-interval', { instrumentToken, interval })
        .catch(() => {
          /* non-fatal */
        })
      delete timers[instrumentToken]
    }, 400)
  }, [])

  useEffect(
    () => () => {
      Object.values(chartZoomSaveTimersRef.current).forEach((tid) => window.clearTimeout(tid))
      Object.values(chartIntervalSaveTimersRef.current).forEach((tid) => window.clearTimeout(tid))
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
      setChartIntervalByToken(coerceChartIntervalOverrideMap(data.intervalByInstrumentToken))
      setFavoriteMlAutomationEnabled(Boolean(data.mlAutomationEnabled))
      setMlAutomationError(null)
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
          mlAutomationEnabled: favoriteMlAutomationEnabled,
        })
        .catch(() => {
          /* non-fatal */
        })
    }, 400)
    return () => window.clearTimeout(t)
  }, [chartInterval, chartRangePreset, chartGraphType, chartPrefsHydrated, favoriteMlAutomationEnabled])

  const loadAutomationRecent = useCallback(async () => {
    try {
      setAutomationRecentLoading(true)
      const { data } = await api.get<MlAutomationRecentRow[]>('/predictions/price-direction/automation-recent', {
        params: { take: 800 },
      })
      setAutomationRecent(data)
    } catch {
      /* non-fatal */
    } finally {
      setAutomationRecentLoading(false)
    }
  }, [])

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

  useEffect(() => {
    if (!isZerodha) {
      setAutomationRecent([])
      return
    }
    void loadAutomationRecent()
    const id = window.setInterval(() => {
      if (document.visibilityState === 'visible') void loadAutomationRecent()
    }, 60_000)
    return () => window.clearInterval(id)
  }, [isZerodha, loadAutomationRecent])

  const liveMarket = useLiveMarketTick(chartRow?.instrumentToken ?? null, isZerodha && mainTab === 'browse' && !!chartRow)
  const liveLtp = liveMarket.lastPrice
  const liveLastTick = liveMarket.lastTick

  const loadInstruments = useCallback(async () => {
    if (!isZerodha) {
      setInstruments(null)
      setInstrumentsError(null)
      setInstrumentsLoading(false)
      setTodayTopPerformers([])
      setTodayTopBasis('')
      setTodayTopError(null)
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

  const loadTodayTopPerformers = useCallback(async () => {
    if (!isZerodha) {
      setTodayTopPerformers([])
      setTodayTopBasis('')
      setTodayTopError(null)
      setTodayTopLoading(false)
      return
    }
    setTodayTopLoading(true)
    setTodayTopError(null)
    try {
      const { data } = await api.get<TodayTopPerformersResponse>(
        '/broker/kite/instruments/today-top-performers',
        { params: { take: 15 } },
      )
      setTodayTopPerformers(data.items ?? [])
      setTodayTopBasis(data.basis ?? '')
    } catch (err) {
      setTodayTopPerformers([])
      setTodayTopBasis('')
      setTodayTopError(problemDetail(err))
    } finally {
      setTodayTopLoading(false)
    }
  }, [isZerodha])

  useEffect(() => {
    if (!isZerodha || instrumentsLoading || !instruments || mainTab !== 'browse') return
    void loadTodayTopPerformers()
  }, [isZerodha, instrumentsLoading, instruments, mainTab, loadTodayTopPerformers])

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
                  Filter preview or press <strong>Enter</strong> / <strong>Search Kite</strong> for a full scan (on{' '}
                  <strong>All favorites</strong>, F&amp;O + MCX). Favorites and chart settings sync to your account; use ☆/★ and{' '}
                  <strong>All favorites</strong> for the grid; on <strong>Browse</strong>, click a row for the chart.
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

            <div className="mt-3 p-3 rounded border border-secondary">
              <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
                <Form.Check
                  type="switch"
                  id="favorite-ml-automation-switch"
                  className="small"
                  label={<span className="fw-semibold">Auto ML for favorites (server)</span>}
                  checked={favoriteMlAutomationEnabled}
                  disabled={!chartPrefsHydrated || mlAutomationSaving}
                  onChange={(e) => {
                    const v = e.target.checked
                    setFavoriteMlAutomationEnabled(v)
                    setMlAutomationSaving(true)
                    setMlAutomationError(null)
                    void api
                      .put('/broker/kite/instruments/favorite-ml-automation', { enabled: v })
                      .then(() => {
                        void loadAutomationRecent()
                      })
                      .catch((err) => setMlAutomationError(problemDetail(err)))
                      .finally(() => setMlAutomationSaving(false))
                  }}
                />
                <Button
                  type="button"
                  variant="outline-secondary"
                  size="sm"
                  disabled={automationRecentLoading || !isZerodha}
                  onClick={() => void loadAutomationRecent()}
                >
                  {automationRecentLoading ? 'Loading…' : 'Refresh list'}
                </Button>
              </div>
              <p className="text-secondary small mb-2">
                When enabled, the API runs scheduled next-bar predictions for each favorite using <strong>every</strong>{' '}
                registered ML engine (comma-separated subset via server{' '}
                <span className="font-monospace">FavoriteMlAutomation:PredictionModelId</span>); LightGBM rows are stored
                separately. Requires Kite session; <strong className="text-body-secondary">FavoriteMlAutomation</strong>{' '}
                must be on in server config.
                By default the server uses <strong>1m</strong> candles for automation so predictions are not held until
                a 3m/5m bar closes; chart interval below still controls what you see on each card.
              </p>
              {mlAutomationError ? (
                <Alert variant="warning" className="py-2 small mb-2">
                  {mlAutomationError}
                </Alert>
              ) : null}
              <div className="small text-secondary text-uppercase mb-1">Recent auto predictions</div>
              <div className="table-responsive" style={{ maxHeight: '220px', overflowY: 'auto' }}>
                <Table striped bordered size="sm" className="mb-0 align-middle">
                  <thead className="table-light">
                    <tr className="text-nowrap">
                      <th>Time</th>
                      <th>Symbol</th>
                      <th>Engine</th>
                      <th>Interval</th>
                      <th>Dir</th>
                      <th>Conf</th>
                      <th>Outcome</th>
                    </tr>
                  </thead>
                  <tbody className="font-monospace">
                    {automationRecent.length === 0 && !automationRecentLoading ? (
                      <tr>
                        <td colSpan={7} className="text-secondary small">
                          No automation rows yet.
                        </td>
                      </tr>
                    ) : (
                      automationRecentSorted.map((r) => (
                        <tr key={r.id}>
                          <td className="small">{formatLocalDateTime(r.predictedAt)}</td>
                          <td>
                            {r.tradingsymbol ? `${r.tradingsymbol}` : r.instrumentToken}
                            {r.exchange ? ` (${r.exchange})` : ''}
                          </td>
                          <td
                            className="text-truncate small"
                            style={{ maxWidth: '8rem' }}
                            title={`Engine: ${r.engineModelId}`}
                          >
                            {r.engineModelId}
                          </td>
                          <td>{r.interval}</td>
                          <td>{r.direction}</td>
                          <td>{r.confidence}</td>
                          <td
                            className={
                              r.outcome === 'correct'
                                ? 'text-success'
                                : r.outcome === 'wrong'
                                  ? 'text-danger'
                                  : 'text-muted'
                            }
                          >
                            {r.outcome}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </Table>
              </div>
            </div>

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
                <div className="mt-4">
                  <div className="d-flex flex-wrap align-items-start justify-content-between gap-2 mb-2">
                    <div className="me-2">
                      <h2 className="h6 mb-1">Today&apos;s top performers</h2>
                      <p className="small text-secondary mb-0" style={{ maxWidth: '44rem' }}>
                        {todayTopBasis ||
                          'Highest % gains vs prior session among the capped preview universe (quotes from Kite /quote/ohlc).'}
                      </p>
                    </div>
                    <Button
                      type="button"
                      variant="outline-secondary"
                      size="sm"
                      disabled={todayTopLoading || instrumentsLoading}
                      onClick={() => void loadTodayTopPerformers()}
                    >
                      {todayTopLoading ? 'Loading…' : 'Refresh movers'}
                    </Button>
                  </div>
                  {todayTopError ? (
                    <Alert variant="warning" className="py-2 small mb-2">
                      {todayTopError}
                    </Alert>
                  ) : null}
                  {todayTopLoading && todayTopPerformers.length === 0 && !todayTopError ? (
                    <p className="text-secondary small mb-2">
                      <Spinner animation="border" size="sm" className="me-2" role="status" />
                      Loading quotes…
                    </p>
                  ) : null}
                  {!todayTopLoading && todayTopPerformers.length === 0 && !todayTopError ? (
                    <p className="text-secondary small mb-0">
                      No movers in the preview snapshot (market closed, no quotes, or Kite returned no overlapping keys).
                    </p>
                  ) : null}
                  {todayTopPerformers.length > 0 ? (
                    <div className="table-responsive">
                      <Table striped bordered hover size="sm" className="mb-0 align-middle">
                        <thead className="table-light">
                          <tr className="text-nowrap">
                            <th>#</th>
                            <th>Symbol</th>
                            <th>Exch</th>
                            <th>LTP</th>
                            <th>Prev close</th>
                            <th>Δ %</th>
                          </tr>
                        </thead>
                        <tbody className="font-monospace">
                          {todayTopPerformers.map((row, idx) => {
                            const instr = kiteInstrumentApiToRow(row.instrument)
                            const pct = Number(row.changePercent)
                            const selected =
                              !!chartRow && favoriteRowKey(chartRow) === favoriteRowKey(instr)
                            return (
                              <tr
                                key={`top-${instr.instrumentToken}-${idx}`}
                                role="button"
                                tabIndex={0}
                                className={selected ? 'table-active' : undefined}
                                style={{ cursor: 'pointer' }}
                                title="Show in chart below"
                                onClick={() => setChartRow(instr)}
                                onKeyDown={(e) => {
                                  if (e.key === 'Enter' || e.key === ' ') {
                                    e.preventDefault()
                                    setChartRow(instr)
                                  }
                                }}
                              >
                                <td className="small text-secondary">{idx + 1}</td>
                                <td className="fw-semibold">{instr.tradingsymbol}</td>
                                <td className="small">{instr.exchange}</td>
                                <td>{Number(row.lastPrice).toFixed(4)}</td>
                                <td>{Number(row.previousClose).toFixed(4)}</td>
                                <td className={pct >= 0 ? 'text-success' : 'text-danger'}>
                                  {pct >= 0 ? '+' : ''}
                                  {pct.toFixed(2)}%
                                </td>
                              </tr>
                            )
                          })}
                        </tbody>
                      </Table>
                    </div>
                  ) : null}
                </div>
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
                  kiteLiveSegmentScope="all"
                  selectedRowKey={chartRow ? favoriteRowKey(chartRow) : null}
                  onSelectRow={setChartRow}
                  favoriteKeySet={favoriteKeySet}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                />
                <FavoritesChartsGrid
                  favorites={favorites}
                  rangePreset={chartRangePreset}
                  onRangePresetChange={setChartRangePreset}
                  defaultInterval={chartInterval}
                  onDefaultIntervalChange={setChartInterval}
                  chartIntervalByInstrumentToken={chartIntervalByToken}
                  onInstrumentIntervalChange={persistInstrumentChartInterval}
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
