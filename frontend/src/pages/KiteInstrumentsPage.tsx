import axios from 'axios'
import {
  useCallback,
  useDeferredValue,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import {
  Alert,
  Badge,
  Button,
  ButtonGroup,
  Card,
  Collapse,
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
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import {
  fetchMergedHistoricalChartCandles,
  type HistoricalChartCandlesResponse,
} from '../api/kiteChartHistorical'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import {
  CHART_FULLSCREEN_META_WRAP_CLASS,
  CHART_FULLSCREEN_META_WRAP_STYLE,
} from '../constants/chartLayout'
import { Layout } from '../components/Layout'
import { ChartZoomControls } from '../components/ChartZoomControls'
import { HistoricalRangeCaption } from '../components/HistoricalRangeCaption'
import { ManualTradeScalperView } from '../components/ManualTradeScalperView'
import { TrendAnalysisMultiPanel } from '../components/TrendAnalysisMultiPanel'
import { InstrumentPriceChart } from '../components/InstrumentPriceChart'
import { ChartWithRightGutter } from '../components/ChartWithRightGutter'
import { useChartFullscreen } from '../hooks/useChartFullscreen'
import { useChartPanPointerHandlers } from '../hooks/useChartPanPointerHandlers'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import type { MarketTickBatchItem } from '../services/marketHub'
import { chartDataIndicesForPaperBuyMarkers } from '../utils/demoPaperBuyBarMarkers'
import type { ChartPointOhlc, ChartIntervalKey, LiveTickVolumeAccumulator } from '../utils/liveCandleMerge'
import { mergeLiveTickIntoOhlc } from '../utils/liveCandleMerge'
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
import {
  CHART_INTERVALS,
  CHART_LIVE_POLL_MS,
  CHART_RANGE_LABEL,
  CHART_RANGE_PRESETS,
  coerceChartGraphType,
  coerceChartInterval,
  coerceChartRangePreset,
  historicalRangeQueryParams,
  type ChartGraphType,
  type ChartInterval,
  type ChartRangePreset,
} from '../utils/kiteInstrumentChartShared'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
import {
  addCustomEmaToChartPoints,
  attachMovingAverages,
  CUSTOM_EMA_DEFAULT_PERIOD,
  CUSTOM_EMA_PERIOD_MAX,
  CUSTOM_EMA_PERIOD_MIN,
  DEFAULT_MA_LINE_VISIBILITY,
  extendYDomainWithLivePrice,
  MA_EMA_FAST_PERIOD,
  MA_EMA_SLOW_PERIOD,
  MA_SMA_PERIOD,
  SR_SWING_PERIOD,
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

type HistoricalCandlesResponse = HistoricalChartCandlesResponse

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

interface KiteTradingLocksResponse {
  items: KiteInstrumentRow[]
}

interface KiteInstrumentsChartSettingsDto {
  interval: string | null
  rangePreset: string | null
  graphType: string | null
  zoomByInstrumentToken?: Record<string, number> | null
  intervalByInstrumentToken?: Record<string, string> | null
  mlAutomationEnabled?: boolean
  /** Per-user automation candle interval (m); omit/null = inherit server override or chart. */
  mlAutomationInterval?: string | null
  /** Per-user N: min whole minutes between new pass starts; omit/null = no extra cadence (intrabar delay may apply). */
  mlAutomationPollIntervalMinutes?: number | null
  /** Per-user seconds after ref bar open before new automation rows; null = use server FavoriteMlAutomation default. */
  mlAutomationMinSecondsAfterBarOpen?: number | null
  /** Saved multi-interval trend checkboxes (chart order); omit on PUT to leave unchanged. */
  trendAnalysisIntervals?: string[] | null
  demoAutoTradeEnabled?: boolean
  /** Fixed demo portfolio size in INR (server-defined). */
  demoAutoTradeNotionalInr?: number
  /** Hypothetical allocation preset id (e.g. equal_split). */
  demoAutoTradeStrategy?: string | null
}

/** GET …/demo-auto-trade/eod-summary — hypothetical same-day outcome from automation rows. */
interface DemoAutoTradeEodSummaryDto {
  reportDateIst: string
  reportTimeZoneId: string
  demoAutoTradeEnabled: boolean
  demoAutoTradeStrategy: string
  demoAutoTradeStrategyTitle: string
  demoNotionalInr: number
  totalSignals: number
  pendingSignals: number
  correctOutcomes: number
  wrongOutcomes: number
  skippedNoNextClose: number
  directionalTradeableLegs: number
  allocatedLegsForPnl: number
  skippedLowConfidenceLegs: number
  demoAutoTradeChargesEnabled: boolean
  demoAutoTradeRoundTripFlatInrPerLeg: number
  demoAutoTradeRoundTripTurnoverBps: number
  hypotheticalGrossPnlInr: number
  hypotheticalChargesInr: number
  hypotheticalTotalPnlInr: number
  pnlAllocationNote: string
  /** Distinct instruments in Locked for trading used to filter rows for demo math. */
  demoAutoTradeLockedInstrumentCount: number
  mayBeTruncated: boolean
}

/** GET …/demo-auto-trade/full-report — multi-day hypothetical demo + slices. */
interface DemoAutoTradeFullReportDailyDto {
  reportDate: string
  totalSignals: number
  pendingSignals: number
  correctOutcomes: number
  wrongOutcomes: number
  skippedNoNextClose: number
  directionalTradeableLegs: number
  allocatedLegsForPnl: number
  skippedLowConfidenceLegs: number
  hypotheticalGrossPnlInr: number
  hypotheticalChargesInr: number
  hypotheticalTotalPnlInr: number
  pnlAllocationNote: string
}

interface DemoAutoTradeFullReportSliceDto {
  key: string
  total: number
  pending: number
  correct: number
  wrong: number
}

interface DemoAutoTradeFullReportDto {
  generatedAtUtc: string
  reportTimeZoneId: string
  fromUtcInclusive: string
  toUtcExclusive: string
  reportRangeSummary: string
  demoAutoTradeEnabled: boolean
  favoriteMlAutomationEnabled: boolean
  demoAutoTradeStrategy: string
  demoAutoTradeStrategyTitle: string
  demoNotionalInrPerDay: number
  demoAutoTradeChargesEnabled: boolean
  demoAutoTradeRoundTripFlatInrPerLeg: number
  demoAutoTradeRoundTripTurnoverBps: number
  dailySummaries: DemoAutoTradeFullReportDailyDto[]
  totalSignalsInRange: number
  pendingSignalsInRange: number
  correctOutcomesInRange: number
  wrongOutcomesInRange: number
  directionalTradeableLegsInRange: number
  hypotheticalGrossPnlInrSummedDays: number
  hypotheticalChargesInrSummedDays: number
  hypotheticalTotalPnlInrSummedDays: number
  directionCountUp: number
  directionCountDown: number
  directionCountNeutral: number
  outcomesByEngine: DemoAutoTradeFullReportSliceDto[]
  outcomesByInterval: DemoAutoTradeFullReportSliceDto[]
  disclaimer: string
  demoAutoTradeLockedInstrumentCount: number
  mayBeTruncated: boolean
}

/** GET …/demo-auto-trade/today-legs — per-signal hypothetical demo legs (report day, trading locks). */
interface DemoAutoTradeLegRowDto {
  predictionId: string
  predictedAtUtc: string
  instrumentToken: string
  tradingsymbol: string | null
  exchange: string | null
  interval: string
  engineModelId: string
  direction: string
  confidence: number
  outcome: string
  refClose: number
  /** Next-bar open when resolved; API sends null for legacy rows. */
  nextOpen: number | null
  nextClose: number | null
  status: string
  statusDetail: string | null
  allocatedNotionalInr: number
  /** Kite contract multiplier from Locked for trading; 0 in legacy fractional-notional mode. */
  instrumentLotMultiplier: number
  demoWholeLotsTraded: number
  committedExposureApproxInr: number
  /** Long/up: buy entry; short/down: exit cover (buy). */
  hypotheticalBuyPrice: number | null
  /** Long/up: exit sale; short/down: short-sale entry. */
  hypotheticalSellPrice: number | null
  legGrossPnlInr: number
  legFeesInr: number
  legNetPnlInr: number
}

interface DemoAutoTradeTodayLegsDto {
  generatedAtUtc: string
  reportDate: string
  reportTimeZoneId: string
  demoAutoTradeEnabled: boolean
  demoAutoTradeStrategy: string
  demoAutoTradeStrategyTitle: string
  demoNotionalInr: number
  demoAutoTradeLockedInstrumentCount: number
  demoAutoTradeChargesEnabled: boolean
  demoAutoTradeRoundTripFlatInrPerLeg: number
  demoAutoTradeRoundTripTurnoverBps: number
  legs: DemoAutoTradeLegRowDto[]
  mayBeTruncated: boolean
}

interface DemoPaperOpenBuyMarkerDto {
  boughtAtUtc: string
  contractsRemaining: number
}

interface DemoPaperPositionListItemDto {
  instrumentToken: string
  tradingsymbol: string
  exchange: string
  lotSize: number | null
  openContracts: number
  openBuys: DemoPaperOpenBuyMarkerDto[]
  /** Latest demo BUY fill when `openContracts` > 0 (server). */
  lastBuyPrice: number | null
}
interface DemoPaperTradeResultDto {
  instrumentToken: string
  tradingsymbol: string
  exchange: string
  side: string
  contracts: number
  lastPrice: number
  lotSize: number
  cashFlowInr: number
  walletBalanceAfter: number
  openContractsAfter: number
}

interface DemoPaperTradeHistoryRowDto {
  id: string
  executedAtUtc: string
  instrumentToken: string
  tradingsymbol: string
  exchange: string
  side: string
  contracts: number
  lastPrice: number
  lotSize: number
  cashFlowInr: number
  walletBalanceAfter: number
  openContractsAfter: number
}

const DEMO_AUTO_TRADE_STRATEGY_OPTIONS: { id: string; label: string; hint: string }[] = [
  {
    id: 'equal_split',
    label: 'Equal risk per signal',
    hint: 'Splits the demo notional evenly across every directional signal with resolved next-bar prices (next open→next close when available, else ref close→next close).',
  },
  {
    id: 'confidence_weighted',
    label: 'Confidence-weighted',
    hint: 'Assigns more hypothetical capital to higher-confidence model outputs (same-day legs only).',
  },
  {
    id: 'high_conviction',
    label: 'High conviction (≥65%)',
    hint: 'Ignores legs under 65% confidence, then divides the full notional across the rest.',
  },
  {
    id: 'one_signal_per_instrument',
    label: 'One leg per symbol',
    hint: 'Keeps only the strongest signal per instrument token to reduce duplicate engines on the same underlying.',
  },
  {
    id: 'signal_strength_squared',
    label: 'Quadratic confidence',
    hint: 'Capital weights scale with confidence² (normalized)—common way to lean harder into the model’s strongest scores.',
  },
  {
    id: 'implied_edge_weighted',
    label: 'Implied edge (fractional)',
    hint: 'Uses weight ∝ max(0, 2×p−1) for p = confidence/100 (toy fractional-Kelly style); signals at or below 50% get no notional.',
  },
  {
    id: 'one_signal_per_engine',
    label: 'One leg per engine',
    hint: 'Per ML engine id, keeps the best directional signal that day—diversifies across models instead of symbols.',
  },
  {
    id: 'top_half_confidence',
    label: 'Top half by confidence',
    hint: 'Keeps only the upper half of directional legs ranked by confidence, then splits notional evenly (median-cut concentration).',
  },
]

function formatInrRupee(amount: number): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    maximumFractionDigits: 0,
  }).format(amount)
}

function formatDemoHypotheticalPrice(px: number | null | undefined): string {
  if (px == null || Number.isNaN(px)) return '—'
  return new Intl.NumberFormat('en-IN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
  }).format(px)
}

function formatDemoAutoTradeLegStatus(status: string): string {
  const m: Record<string, string> = {
    pending: 'Pending',
    allocated: 'Allocated',
    excluded_neutral: 'Excluded · neutral',
    excluded_no_price: 'Excluded · no price',
    excluded_not_directional: 'Excluded · direction',
    excluded_low_confidence: 'Excluded · confidence',
    excluded_by_strategy: 'Excluded · preset',
    excluded_zero_allocation: 'Excluded · zero weight',
    excluded_missing_lot_size: 'Excluded · lot size',
    excluded_cannot_buy_one_lot: 'Excluded · below 1 lot',
  }
  return m[status] ?? status
}

function ManualPaperTradePanel({
  heading,
  intro,
  showWalletLine,
  walletBalanceInr,
  isZerodha,
  tradingLocks,
  demoPaperToken,
  setDemoPaperToken,
  demoPaperContracts,
  setDemoPaperContracts,
  demoPaperTradeBusy,
  demoPaperTradeError,
  demoPaperTradeLast,
  demoPaperPositions,
  demoPaperPositionsLoading,
  demoPaperPositionsError,
  demoPaperTrades,
  demoPaperTradesLoading,
  demoPaperTradesError,
  executeDemoPaperTrade,
  loadDemoPaperPositions,
  loadDemoPaperTrades,
}: {
  heading: ReactNode
  intro: ReactNode
  showWalletLine: boolean
  walletBalanceInr: number
  isZerodha: boolean
  tradingLocks: KiteInstrumentRow[]
  /** Row matching this token is subtly highlighted (e.g. same as scalper chart selection). */
  demoPaperToken: string
  setDemoPaperToken: (token: string) => void
  demoPaperContracts: string
  setDemoPaperContracts: (v: string) => void
  demoPaperTradeBusy: { side: 'buy' | 'sell'; instrumentToken: string } | null
  demoPaperTradeError: string | null
  demoPaperTradeLast: string | null
  demoPaperPositions: DemoPaperPositionListItemDto[]
  demoPaperPositionsLoading: boolean
  demoPaperPositionsError: string | null
  demoPaperTrades: DemoPaperTradeHistoryRowDto[]
  demoPaperTradesLoading: boolean
  demoPaperTradesError: string | null
  executeDemoPaperTrade: (side: 'buy' | 'sell', instrumentToken: string) => void
  loadDemoPaperPositions: () => Promise<void>
  loadDemoPaperTrades: () => Promise<void>
}) {
  const idPrefix = useId()

  const paperOpenByToken = useMemo(() => {
    const m = new Map<string, DemoPaperPositionListItemDto>()
    for (const p of demoPaperPositions) {
      if (p.instrumentToken?.trim()) m.set(p.instrumentToken.trim(), p)
    }
    return m
  }, [demoPaperPositions])

  const tradingInFlight = demoPaperTradeBusy !== null

  return (
    <div>
      {heading}
      {intro}
      {showWalletLine ? (
        <p className="small mb-3">
          <span className="fw-semibold">Wallet balance:</span>{' '}
          <strong>{formatInrRupee(walletBalanceInr)}</strong>
          {' — '}
          <Link to="/wallet">Load funds</Link>
          {' · '}
          <Link to="/instruments?tab=locked">Locks</Link>
        </p>
      ) : null}
      {demoPaperTradeError ? (
        <Alert variant="danger" className="py-2 small">
          {demoPaperTradeError}
        </Alert>
      ) : null}
      {demoPaperTradeLast ? (
        <Alert variant="success" className="py-2 small">
          {demoPaperTradeLast}
        </Alert>
      ) : null}
      {demoPaperPositionsError ? (
        <Alert variant="warning" className="py-2 small">
          {demoPaperPositionsError}
        </Alert>
      ) : null}
      <Row className="g-2 align-items-end mb-3">
        <Col xs={6} sm={4} md={3}>
          <Form.Group controlId={`${idPrefix}-paper-lots`} className="mb-0">
            <Form.Label className="small text-secondary mb-1">Lots (per Buy / Sell)</Form.Label>
            <Form.Control
              size="sm"
              type="text"
              inputMode="numeric"
              autoComplete="off"
              value={demoPaperContracts}
              onChange={(e) => setDemoPaperContracts(e.target.value)}
              disabled={tradingInFlight}
              aria-describedby={`${idPrefix}-lots-help`}
            />
          </Form.Group>
        </Col>
        <Col xs={12} sm="auto" className="d-flex align-items-end pb-1">
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            disabled={demoPaperPositionsLoading || demoPaperTradesLoading || tradingInFlight}
            onClick={() => void Promise.all([loadDemoPaperPositions(), loadDemoPaperTrades()])}
          >
            {demoPaperPositionsLoading || demoPaperTradesLoading ? '…' : 'Refresh positions & history'}
          </Button>
        </Col>
      </Row>
      <Form.Text id={`${idPrefix}-lots-help`} className="text-muted d-block small mb-2">
        One value for every <strong>Buy</strong>/<strong>Sell</strong>. Each <strong>lot</strong> = Kite{' '}
        <strong>lot size × LTP</strong> (cash debited/credited). Quantity in shares/units for that fill ={' '}
        <strong>lots × lot size</strong>. Highlighted row matches the scalper symbol (click symbol to select).
      </Form.Text>
      {!isZerodha || tradingLocks.length === 0 ? (
        <p className="small text-muted mb-0 py-3 border rounded px-3 bg-body-tertiary">
          {tradingLocks.length === 0 ? (
            <>
              No locks — add instruments on <Link to="/instruments?tab=locked">Locked for trading</Link>.
            </>
          ) : (
            'Connect Zerodha to trade.'
          )}
        </p>
      ) : (
        <div className="table-responsive border rounded shadow-sm mb-3">
          <Table striped hover size="sm" className="mb-0 align-middle">
            <thead className="table-light">
              <tr className="small text-secondary">
                <th scope="col">Symbol</th>
                <th scope="col">Exch.</th>
                <th scope="col">Type</th>
                <th scope="col">Lot</th>
                <th scope="col" className="text-end">
                  Open (lots)
                </th>
                <th scope="col" className="text-end text-nowrap">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody>
              {tradingLocks.map((r) => {
                const t = r.instrumentToken.trim()
                const pos = paperOpenByToken.get(t)
                const openLong = pos?.openContracts ?? 0
                const rowBusyBuy =
                  demoPaperTradeBusy?.side === 'buy' && demoPaperTradeBusy?.instrumentToken === t
                const rowBusySell =
                  demoPaperTradeBusy?.side === 'sell' && demoPaperTradeBusy?.instrumentToken === t
                const rowSelected = demoPaperToken.trim().length > 0 && demoPaperToken.trim() === t
                return (
                  <tr key={t} className={rowSelected ? 'table-primary' : undefined}>
                    <td className="font-monospace">
                      <button
                        type="button"
                        className={`btn btn-link btn-sm text-start text-decoration-none p-0 lh-sm ${
                          rowSelected ? 'fw-bold text-primary' : 'text-body'
                        }`}
                        title="Use this lock for scalper + highlight"
                        onClick={() => setDemoPaperToken(t)}
                      >
                        {r.tradingsymbol}
                      </button>
                    </td>
                    <td className="small">{r.exchange}</td>
                    <td className="small font-monospace text-secondary">{r.instrumentType?.trim() || '—'}</td>
                    <td className="small font-monospace text-secondary">{r.lotSize != null ? r.lotSize : '—'}</td>
                    <td className="text-end small font-monospace">{Number.isFinite(openLong) ? openLong : '—'}</td>
                    <td className="text-end">
                      <div className="d-inline-flex flex-wrap gap-1 justify-content-end">
                        <Button
                          type="button"
                          variant="success"
                          size="sm"
                          className="py-0 px-2 text-nowrap"
                          disabled={!isZerodha || tradingInFlight}
                          aria-label={`Paper buy ${r.tradingsymbol}`}
                          onClick={() => {
                            setDemoPaperToken(t)
                            void executeDemoPaperTrade('buy', t)
                          }}
                        >
                          {rowBusyBuy ? '…' : 'Buy'}
                        </Button>
                        <Button
                          type="button"
                          variant="outline-danger"
                          size="sm"
                          className="py-0 px-2 text-nowrap"
                          disabled={!isZerodha || tradingInFlight}
                          aria-label={`Paper sell ${r.tradingsymbol}`}
                          onClick={() => {
                            setDemoPaperToken(t)
                            void executeDemoPaperTrade('sell', t)
                          }}
                        >
                          {rowBusySell ? '…' : 'Sell'}
                        </Button>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </Table>
        </div>
      )}
      <div className="mt-4">
        <p className="small text-muted mb-2 fw-semibold">Trade history (demo paper)</p>
        {demoPaperTradesError ? (
          <Alert variant="warning" className="py-2 small mb-2">
            {demoPaperTradesError}
          </Alert>
        ) : null}
        {demoPaperTradesLoading && demoPaperTrades.length === 0 ? (
          <p className="small text-muted mb-0 py-2">
            <Spinner animation="border" size="sm" className="me-2" role="status" />
            Loading trade history…
          </p>
        ) : demoPaperTrades.length === 0 ? (
          <p className="small text-muted mb-0 py-2 border rounded px-3 bg-body-tertiary">
            No demo paper fills yet. Buys and sells from the table above appear here (newest first).
          </p>
        ) : (
          <div
            className="table-responsive border rounded shadow-sm"
            style={{ maxHeight: 'min(380px, 45vh)', overflowY: 'auto' }}
          >
            <Table striped hover size="sm" className="mb-0 align-middle small">
              <thead className="table-light">
                <tr className="text-secondary text-nowrap">
                  <th scope="col">Time</th>
                  <th scope="col">Side</th>
                  <th scope="col">Symbol</th>
                  <th scope="col">Ex</th>
                  <th scope="col" className="text-end">
                    Lots
                  </th>
                  <th scope="col" className="text-end">
                    LTP
                  </th>
                  <th scope="col" className="text-end">
                    Lot
                  </th>
                  <th scope="col" className="text-end">
                    Cash Δ
                  </th>
                  <th scope="col" className="text-end">
                    Wallet after
                  </th>
                  <th scope="col" className="text-end">
                    Open lots after
                  </th>
                </tr>
              </thead>
              <tbody>
                {demoPaperTrades.map((row) => {
                  const cf = Number(row.cashFlowInr)
                  const cfClass =
                    Number.isFinite(cf) && cf > 0 ? 'text-success' : Number.isFinite(cf) && cf < 0 ? 'text-danger' : ''
                  const sideNorm = row.side?.trim().toLowerCase()
                  return (
                    <tr key={row.id}>
                      <td className="text-nowrap">{formatLocalDateTime(row.executedAtUtc)}</td>
                      <td>
                        {sideNorm === 'buy' ? (
                          <Badge bg="success">Buy</Badge>
                        ) : sideNorm === 'sell' ? (
                          <Badge bg="danger">Sell</Badge>
                        ) : (
                          <Badge bg="secondary">{row.side ?? '—'}</Badge>
                        )}
                      </td>
                      <td className="font-monospace">{row.tradingsymbol}</td>
                      <td className="small">{row.exchange}</td>
                      <td className="text-end font-monospace">{row.contracts}</td>
                      <td className="text-end font-monospace">{row.lastPrice}</td>
                      <td className="text-end font-monospace">{row.lotSize}</td>
                      <td className={`text-end font-monospace ${cfClass}`}>
                        {Number.isFinite(cf) && cf > 0 ? '+' : ''}
                        {formatInrRupee(cf)}
                      </td>
                      <td className="text-end font-monospace">{formatInrRupee(Number(row.walletBalanceAfter))}</td>
                      <td className="text-end font-monospace">{row.openContractsAfter}</td>
                    </tr>
                  )
                })}
              </tbody>
            </Table>
          </div>
        )}
      </div>
      <p className="small text-muted mb-1 fw-semibold mt-4">Open positions detail</p>
      {demoPaperPositions.length > 0 ? (
        <div className="mt-1 small border rounded px-3 py-2 bg-body-tertiary">
          {demoPaperPositions.map((p, idx) => (
            <div
              key={p.instrumentToken}
              className={`font-monospace py-1 ${idx < demoPaperPositions.length - 1 ? 'border-bottom border-secondary-subtle' : ''}`}
            >
              {p.tradingsymbol} · open {p.openContracts} lot{p.openContracts === 1 ? '' : 's'}
              {p.lotSize != null ? ` · lot ${p.lotSize}` : ''}
            </div>
          ))}
        </div>
      ) : (
        <p className="small text-muted mb-0">No open paper longs.</p>
      )}
    </div>
  )
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
  /** Next-bar open when resolved from Kite candles (demo P&L uses this as entry when set). */
  nextOpen: number | null
  nextClose: number | null
  /** Registered prediction engine id for this automation row (which model slot was invoked). */
  engineModelId: string
}

/** POST /predictions/price-direction/automation-report-email */
interface ManualAutomationEmailReportResponse {
  rowCount: number
  reportRangeSummary: string
  pieChartsAttached: number
  totalAttachmentsSent: number
}

function pad2DatetimeLocalComponent(n: number): string {
  return String(n).padStart(2, '0')
}

/** Builds `yyyy-MM-ddTHH:mm` for `<input type="datetime-local">` from local wall time. */
function dateToDatetimeLocalInputValue(d: Date): string {
  return `${d.getFullYear()}-${pad2DatetimeLocalComponent(d.getMonth() + 1)}-${pad2DatetimeLocalComponent(d.getDate())}T${pad2DatetimeLocalComponent(d.getHours())}:${pad2DatetimeLocalComponent(d.getMinutes())}`
}

/** Local calendar day containing “now”: midnight → current minute (for Auto predictions range + list fetch). */
function initialAutomationEmailReportDatetimeLocal(): { from: string; to: string } {
  const to = new Date()
  to.setSeconds(0, 0)
  const from = new Date(to.getFullYear(), to.getMonth(), to.getDate(), 0, 0, 0, 0)
  return { from: dateToDatetimeLocalInputValue(from), to: dateToDatetimeLocalInputValue(to) }
}

const TRADER_TREND_ANALYSIS_INTERVALS_LS = 'trader-trend-analysis-intervals'

/** Preserve chart order — only validated codes from storage. */
function loadTrendAnalysisSelections(): ChartInterval[] {
  try {
    if (typeof window === 'undefined') return [...CHART_INTERVALS]
    const raw = window.localStorage.getItem(TRADER_TREND_ANALYSIS_INTERVALS_LS)
    if (!raw) return [...CHART_INTERVALS]
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed) || parsed.length === 0) return [...CHART_INTERVALS]
    const next: ChartInterval[] = []
    for (const entry of parsed) {
      const s = String(entry ?? '')
      if ((CHART_INTERVALS as readonly string[]).includes(s)) next.push(s as ChartInterval)
    }
    return next.length > 0 ? next : [...CHART_INTERVALS]
  } catch {
    return [...CHART_INTERVALS]
  }
}

function orderTrendSelections(s: Iterable<ChartInterval>): ChartInterval[] {
  const set = new Set(s)
  return CHART_INTERVALS.filter((iv) => set.has(iv))
}

/** Sort automation row interval codes in chart order, then unknown codes alphabetically. */
function sortMlAutomationIntervalCodes(intervals: string[]): string[] {
  const order = CHART_INTERVALS as readonly string[]
  return [...intervals].sort((a, b) => {
    const ia = order.indexOf(a)
    const ib = order.indexOf(b)
    if (ia !== -1 && ib !== -1) return ia - ib
    if (ia !== -1) return -1
    if (ib !== -1) return 1
    return a.localeCompare(b)
  })
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

type MainTab =
  | 'browse'
  | 'favorites'
  | 'tradingLocks'
  | 'automation'
  | 'manualTrade'
  | 'autoTrading'

/** Deep-link: <code>?tab=favorites</code> …; <code>?tab=manual-trade</code>; <code>?tab=demo-auto-trade</code>; <code>?tab=locked</code>. */
function mainTabFromSearchParams(params: URLSearchParams): MainTab {
  const raw = params.get('tab')
  const tab = raw?.toLowerCase()
  if (!tab) {
    const fav = params.get('fav')
    const favOpen =
      fav === '1' || (fav != null && fav.toLowerCase() === 'true')
    return favOpen ? 'favorites' : 'browse'
  }
  if (tab === 'favorites' || tab === 'fav') return 'favorites'
  if (tab === 'locked' || tab === 'trading-locks' || tab === 'tradinglocks') return 'tradingLocks'
  if (
    tab === 'manual-trade' ||
    tab === 'manualtrade' ||
    tab === 'manual' ||
    tab === 'paper-trade' ||
    tab === 'papertrade'
  )
    return 'manualTrade'
  if (
    tab === 'demo-auto-trade' ||
    tab === 'demoautotrade' ||
    tab === 'autotrading' ||
    tab === 'auto-trading' ||
    tab === 'demo-trade'
  )
    return 'autoTrading'
  if (tab === 'automation' || tab === 'auto' || tab === 'auto-ml' || tab === 'automl') return 'automation'
  return 'browse'
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

const INSTRUMENT_PAGE_SIZE = 50
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

/** Mirrors server-side Kite search: whitespace = AND phrases; each phrase splits into letter runs + digit runs. */
function expandInstrumentSearchTokens(raw: string): string[] {
  const t = raw.trim().toLowerCase().replace(/\+/g, ' ')
  const segments = t.split(/\s+/).filter(Boolean)
  const tokens: string[] = []
  const partRe = /\d+|[a-z]+/g
  const aliases: Record<string, string> = {
    nity: 'nifty',
    bnfty: 'banknifty',
  }
  for (const seg of segments) {
    let m: RegExpExecArray | null
    partRe.lastIndex = 0
    while ((m = partRe.exec(seg)) !== null) {
      const v = aliases[m[0]] ?? m[0]
      if (v.length > 0) tokens.push(v)
    }
  }
  return tokens
}

function rowMatchesInstrumentSearchTokens(haystackLower: string, tokens: string[]): boolean {
  return tokens.every((tok) => haystackLower.includes(tok))
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
  tradingLockKeySet,
  onToggleTradingLock,
}: {
  title: string
  rows: KiteInstrumentRow[]
  truncated: boolean
  loading: boolean
  emptyHint: string
  searchSegment: 'fno' | 'mcx' | 'spot'
  /** `panel`: one server scan for this panel's segment. `all`: F&O + Spot + MCX (e.g. favorites). */
  kiteLiveSegmentScope?: 'panel' | 'all'
  selectedRowKey: string | null
  onSelectRow: (row: KiteInstrumentRow) => void
  enableKiteLiveSearch?: boolean
  favoriteKeySet: Set<string>
  onToggleFavorite: (row: KiteInstrumentRow) => void
  tradingLockKeySet?: Set<string>
  onToggleTradingLock?: (row: KiteInstrumentRow) => void
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
    const q = deferredSearch.trim()
    if (!q) return rows
    const tokens = expandInstrumentSearchTokens(deferredSearch)
    if (tokens.length === 0) return rows
    return rows.filter((r) => rowMatchesInstrumentSearchTokens(rowSearchHaystack(r), tokens))
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
      const segments: Array<'fno' | 'mcx' | 'spot'> =
        kiteLiveSegmentScope === 'all' ? ['fno', 'spot', 'mcx'] : [searchSegment]
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
          List may be incomplete — nearest expiry rows first from a capped CSV buffer; use Search Kite for exact contracts.
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
              placeholder="Symbol or compact token runs (e.g. nifty 25 may)"
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
                {onToggleTradingLock ? (
                  <th className="text-center" style={{ width: '2.5rem' }} title="Locked for trading">
                    🔒
                  </th>
                ) : null}
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
                  {onToggleTradingLock ? (
                    <td
                      className="text-center align-middle"
                      onClick={(e) => {
                        e.stopPropagation()
                        onToggleTradingLock(r)
                      }}
                    >
                      <Button
                        type="button"
                        variant="link"
                        className="p-0 text-info text-decoration-none lh-1"
                        aria-label={
                          (tradingLockKeySet?.has(favoriteRowKey(r)) ?? false)
                            ? 'Unlock for trading'
                            : 'Lock for trading'
                        }
                        aria-pressed={tradingLockKeySet?.has(favoriteRowKey(r)) ?? false}
                        tabIndex={0}
                        style={{ fontSize: '1rem' }}
                        onKeyDown={(e) => e.stopPropagation()}
                      >
                        {(tradingLockKeySet?.has(favoriteRowKey(r)) ?? false) ? '🔒' : '🔓'}
                      </Button>
                    </td>
                  ) : null}
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


/** Caption strip + sticky thead + ~5 tbody rows; additional rows scroll inside the box. */
const ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT_COMPACT = 'calc(1.85rem + 2.1rem + (5 * 2rem))'
const ML_PREDICTION_HISTORY_SCROLL_MAX_HEIGHT = 'calc(2rem + 2.35rem + (5 * 2.15rem))'
/** Matches server-side PriceDirectionPredictionService.MaxAutomationHistoryTake (merged classic + LightGBM). */
const ML_AUTOMATION_RECENT_FETCH_TAKE = 5000
const ML_AUTOMATION_TABLE_MAX_HEIGHT = 'min(780px, 75vh)'
/** Matches server max take for today-top-performers; Browse shows TODAY_TOP_MOVERS_PAGE_SIZE rows then Load more. */
const TODAY_TOP_MOVERS_FETCH_TAKE = 30
const TODAY_TOP_MOVERS_PAGE_SIZE = 5

type ChartPoint = ChartPointOhlc

/** Re-fetch OHLC while a chart is mounted (browser tab visible) to keep the series current. */
// Per-chart historical-OHLC refresh cadence. With many favorite/locked tiles open this
// multiplies (one timer per tile) so 60s keeps the call rate well under broker quotas
// while still feeling "live" alongside the websocket tick overlay. See CHART_LIVE_POLL_MS in kiteInstrumentChartShared.

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

function ChartSettingsToolbar({
  idPrefix,
  rangePreset,
  onRangePresetChange,
  interval,
  onIntervalChange,
  trendAnalysisSelections,
  onTrendAnalysisSelectionsChange,
  graphType,
  onGraphTypeChange,
  maLineVisibility,
  onMaLineVisibilityChange,
  customEmaPeriod,
  onCustomEmaPeriodChange,
  trendPresetHint,
}: {
  idPrefix: string
  rangePreset: ChartRangePreset
  onRangePresetChange: (v: ChartRangePreset) => void
  interval: ChartInterval
  /** Main chart OHLC timeframe (radio row below). */
  onIntervalChange: (v: ChartInterval) => void
  trendAnalysisSelections: ChartInterval[]
  onTrendAnalysisSelectionsChange: (next: ChartInterval[]) => void
  graphType: ChartGraphType
  onGraphTypeChange: (v: ChartGraphType) => void
  maLineVisibility: MaLineVisibility
  onMaLineVisibilityChange: (patch: Partial<MaLineVisibility>) => void
  customEmaPeriod: number
  onCustomEmaPeriodChange: (n: number) => void
  /** Tooltip for Trend analysis presets (e.g. All favorites = sync every chart). */
  trendPresetHint?: string
}) {
  const trendSet = useMemo(() => new Set(trendAnalysisSelections), [trendAnalysisSelections])
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
        <span className="small text-secondary text-uppercase me-1">Trend analysis</span>
        <ButtonGroup size="sm" className="flex-wrap">
          {CHART_INTERVALS.map((iv) => (
            <ToggleButton
              key={`trend-${iv}`}
              id={`${idPrefix}-trend-${iv}`}
              type="checkbox"
              variant={trendSet.has(iv) ? 'primary' : 'outline-primary'}
              value={iv}
              checked={trendSet.has(iv)}
              title={
                (trendPresetHint ?? '').length > 0
                  ? trendPresetHint
                  : 'Past-data multi-timeframe (LR on close — same Range as chart); check any combination'
              }
              onChange={(e) => {
                const sel = e.currentTarget.checked
                const nextSet = new Set(trendAnalysisSelections)
                if (sel) nextSet.add(iv)
                else nextSet.delete(iv)
                onTrendAnalysisSelectionsChange(orderTrendSelections(nextSet))
              }}
            >
              {iv}
            </ToggleButton>
          ))}
        </ButtonGroup>
        <ButtonGroup size="sm">
          <Button
            type="button"
            variant="outline-primary"
            size="sm"
            className="py-1"
            title="Analyze all chart intervals"
            onClick={() => onTrendAnalysisSelectionsChange([...CHART_INTERVALS])}
          >
            All
          </Button>
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            className="py-1"
            title="Clear trend picks (tables ask you to re-select)"
            onClick={() => onTrendAnalysisSelectionsChange([])}
          >
            Clear
          </Button>
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
          <ToggleButton
            id={`${idPrefix}-ind-lintrend`}
            type="checkbox"
            variant={maLineVisibility.showLinearCloseTrend ? 'secondary' : 'outline-secondary'}
            value="linearTrend"
            checked={maLineVisibility.showLinearCloseTrend}
            title="Least-squares regression on close (candles only)"
            onChange={(e) => onMaLineVisibilityChange({ showLinearCloseTrend: e.currentTarget.checked })}
          >
            Trend LR
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
type MlDirectionBias = 'up' | 'down' | 'neutral'

const ML_FULLSCREEN_PIE_HEIGHT = { compact: 400, default: 480 } as const
const ML_FULLSCREEN_PIE_MIN_COL_PX = 460

/** Group history rows by <code>modelId</code> and tally outcomes. */
function outcomeCountsForModelId(rows: readonly MlPredictionLogEntry[], modelId: string): MlOutcomeCounts {
  let correct = 0
  let wrong = 0
  let pending = 0
  const id = modelId.trim()
  for (const e of rows) {
    if ((e.modelId?.trim() || '') !== id) continue
    if (e.outcome === 'correct') correct++
    else if (e.outcome === 'wrong') wrong++
    else pending++
  }
  return { correct, wrong, pending }
}

function mergedOutcomeCountsForModel(
  classic: readonly MlPredictionLogEntry[],
  lgbm: readonly MlPredictionLogEntry[],
  modelId: string,
): MlOutcomeCounts {
  const a = outcomeCountsForModelId(classic, modelId)
  const b = outcomeCountsForModelId(lgbm, modelId)
  return {
    correct: a.correct + b.correct,
    wrong: a.wrong + b.wrong,
    pending: a.pending + b.pending,
  }
}

/** Accuracy = correct / (correct + wrong), as integer percent. Returns <c>null</c> when no rows are resolved (only pending). */
function accuracyPercentFromCounts(counts: MlOutcomeCounts): number | null {
  const resolved = counts.correct + counts.wrong
  if (resolved === 0) return null
  return Math.round((counts.correct / resolved) * 100)
}

/** Tally automation rows for a registered engine (<code>engineModelId</code>). */
function outcomeCountsForAutomationEngine(
  rows: readonly MlAutomationRecentRow[],
  engineModelId: string,
): MlOutcomeCounts {
  let correct = 0
  let wrong = 0
  let pending = 0
  const id = engineModelId.trim()
  for (const r of rows) {
    if ((r.engineModelId?.trim() || '') !== id) continue
    if (r.outcome === 'correct') correct++
    else if (r.outcome === 'wrong') wrong++
    else pending++
  }
  return { correct, wrong, pending }
}

/** Server registry order first, then any extra <code>engineModelId</code> values seen in automation rows. */
function orderedAutomationEngineIds(
  priceModels: PriceDirectionModelsApiResponse | null,
  rowsForIds: readonly MlAutomationRecentRow[],
): string[] {
  const fromApi = priceModels?.models?.map((m) => m.id.trim()).filter((sid) => sid.length > 0) ?? []
  const seen = new Set(fromApi)
  const extras: string[] = []
  for (const r of rowsForIds) {
    const sid = r.engineModelId?.trim()
    if (sid && !seen.has(sid)) {
      seen.add(sid)
      extras.push(sid)
    }
  }
  extras.sort((a, b) => a.localeCompare(b))
  return [...fromApi, ...extras]
}

const ML_AUTOMATION_PIE_HEIGHT = 320

/** Pies merge correct / wrong / pending for each registered engine; <code>rows</code> are already filtered. */
function MlAutomationOutcomesPieGrid({
  rows,
  priceModels,
}: {
  rows: readonly MlAutomationRecentRow[]
  priceModels: PriceDirectionModelsApiResponse | null
}) {
  const [piesExpanded, setPiesExpanded] = useState(false)
  const outcomesPiesCollapseId = useId()
  const modelIds = useMemo(
    () => orderedAutomationEngineIds(priceModels, rows),
    [priceModels, rows],
  )
  const descById = useMemo(() => {
    const m = new Map<string, string>()
    for (const x of priceModels?.models ?? []) {
      const id = x.id?.trim()
      if (id) m.set(id, x.description)
    }
    return m
  }, [priceModels])

  if (modelIds.length === 0) return null

  return (
    <div className="flex-shrink-0 overflow-visible mb-3">
      <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2">
        <div className="small text-muted text-uppercase mb-0" style={{ fontSize: '0.68rem' }}>
          Outcomes by engine — {modelIds.length} model{modelIds.length === 1 ? '' : 's'} (respects search + direction +
          outcome + interval + engine)
        </div>
        <Button
          type="button"
          variant="outline-secondary"
          size="sm"
          className="text-nowrap"
          aria-expanded={piesExpanded}
          aria-controls={outcomesPiesCollapseId}
          onClick={() => setPiesExpanded((v) => !v)}
        >
          {piesExpanded ? 'Minimise pies' : 'Expand pies'}
        </Button>
      </div>
      {!piesExpanded ? (
        <p className="small text-secondary mb-0">
          Charts minimised — use <strong>Expand pies</strong> to show outcome distribution per engine.
        </p>
      ) : null}
      <Collapse in={piesExpanded} mountOnEnter unmountOnExit>
        <div
          id={outcomesPiesCollapseId}
          className="d-grid gap-3"
          style={{
            gridTemplateColumns: `repeat(auto-fill, minmax(min(100%, ${ML_FULLSCREEN_PIE_MIN_COL_PX}px), 1fr))`,
          }}
        >
        {modelIds.map((engineId) => {
          const counts = outcomeCountsForAutomationEngine(rows, engineId)
          const n = counts.correct + counts.wrong + counts.pending
          const resolved = counts.correct + counts.wrong
          const accuracyPct = accuracyPercentFromCounts(counts)
          const desc = descById.get(engineId)
          return (
            <div key={engineId} className="flex-shrink-0 overflow-visible border border-secondary rounded p-3 bg-body-tertiary">
              <div
                className="small fw-semibold text-truncate mb-1 font-monospace"
                title={`${engineId} — ${n} row(s); ${counts.correct} correct / ${counts.wrong} wrong / ${counts.pending} pending`}
              >
                {engineId}
                <span className="text-muted fw-normal ms-1">({n})</span>
              </div>
              <div
                className={`small mb-2 fw-semibold ${
                  accuracyPct == null
                    ? 'text-muted'
                    : accuracyPct >= 50
                      ? 'text-success'
                      : 'text-danger'
                }`}
                title="Accuracy = correct / (correct + wrong); pending rows excluded"
              >
                Prediction:{' '}
                {accuracyPct != null
                  ? `${accuracyPct}% (${counts.correct}/${resolved} resolved)`
                  : 'no resolved rows yet'}
                {counts.pending > 0 ? (
                  <span className="text-muted fw-normal ms-2">· {counts.pending} pending</span>
                ) : null}
              </div>
              {desc ? (
                <div className="text-muted mb-2" style={{ fontSize: '0.78rem', lineHeight: 1.35 }}>
                  {desc}
                </div>
              ) : null}
              <MlOutcomePieChart counts={counts} height={ML_AUTOMATION_PIE_HEIGHT} />
            </div>
          )
        })}
        </div>
      </Collapse>
    </div>
  )
}

/** Server registry order first, then any extra model IDs seen in histories. */
function orderedModelIdsForFullscreenPies(
  priceModels: PriceDirectionModelsApiResponse | null,
  classic: readonly MlPredictionLogEntry[],
  lgbm: readonly MlPredictionLogEntry[],
): string[] {
  const fromApi = priceModels?.models?.map((m) => m.id.trim()).filter((id) => id.length > 0) ?? []
  const seen = new Set(fromApi)
  const extras: string[] = []
  for (const e of classic) {
    const id = e.modelId?.trim()
    if (id && !seen.has(id)) {
      seen.add(id)
      extras.push(id)
    }
  }
  for (const e of lgbm) {
    const id = e.modelId?.trim()
    if (id && !seen.has(id)) {
      seen.add(id)
      extras.push(id)
    }
  }
  extras.sort((a, b) => a.localeCompare(b))
  return [...fromApi, ...extras]
}

function MlOutcomePieChart({
  counts,
  height,
  maxWidth,
}: {
  counts: MlOutcomeCounts
  height: number
  /** When set, caps chart width; omit to fill the parent (e.g. grid cells). */
  maxWidth?: number
}) {
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
        className="d-flex align-items-center justify-content-center border border-secondary rounded bg-body-secondary text-secondary small flex-shrink-0"
        style={{ height, ...(maxWidth != null ? { maxWidth } : {}) }}
      >
        No predictions to chart
      </div>
    )
  }

  return (
    <div
      className="flex-shrink-0 w-100 overflow-visible"
      style={{
        ...(maxWidth != null ? { maxWidth } : {}),
        height,
        minHeight: height,
      }}
    >
      {/* Extra bottom space + horizontal margins avoid SVG clipping where slice labels/Legend used to collide with the view edge */}
      <ChartWithRightGutter>
        <ResponsiveContainer width="100%" height="100%" debounce={50}>
          <PieChart margin={{ top: 10, left: 12, right: 12, bottom: 44 }}>
          <Pie
            data={pieData}
            dataKey="value"
            nameKey="name"
            cx="50%"
            cy="44%"
            innerRadius={0}
            outerRadius="62%"
            paddingAngle={pieData.length > 1 ? 1.5 : 0}
          >
            {pieData.map((d) => (
              <Cell key={d.name} fill={d.fill} />
            ))}
          </Pie>
          <Tooltip
            formatter={(value: number, name: string) => [`${value} row(s)`, name]}
          />
          <Legend
            verticalAlign="bottom"
            align="center"
            layout="horizontal"
            wrapperStyle={{ fontSize: 12, paddingTop: 4, overflow: 'visible' }}
          />
        </PieChart>
        </ResponsiveContainer>
      </ChartWithRightGutter>
    </div>
  )
}

function countMlDirections(directions: readonly MlDirectionBias[]): {
  up: number
  down: number
  neutral: number
} {
  let up = 0
  let down = 0
  let neutral = 0
  for (const d of directions) {
    if (d === 'up') up += 1
    else if (d === 'down') down += 1
    else neutral += 1
  }
  return { up, down, neutral }
}

/** Single top direction; ties (e.g. 1 up, 1 down, 1 neutral) resolve to neutral. */
function consensusMlDirection(counts: {
  up: number
  down: number
  neutral: number
}): MlDirectionBias | null {
  const { up, down, neutral: neu } = counts
  const total = up + down + neu
  if (total === 0) return null
  const max = Math.max(up, down, neu)
  const winners: MlDirectionBias[] = []
  if (up === max) winners.push('up')
  if (down === max) winners.push('down')
  if (neu === max) winners.push('neutral')
  if (winners.length === 1) return winners[0]
  return 'neutral'
}

const ML_AUTOMATION_DIRECTION_VOTE_PIE_HEIGHT = 240

/** Direction distribution + plurality consensus for the **filtered** automation rows (same set as the recent table). */
function MlAutomationDirectionVotePie({
  rows,
  totalLoaded,
}: {
  rows: readonly MlAutomationRecentRow[]
  /** Rows returned from server before search + table filters (for caption). */
  totalLoaded: number
}) {
  const [pieExpanded, setPieExpanded] = useState(false)
  const directionPieCollapseId = useId()
  const directions = useMemo(
    () =>
      rows.map((r) => r.direction as MlDirectionBias).filter((d) => d === 'up' || d === 'down' || d === 'neutral'),
    [rows],
  )
  const counts = useMemo(() => countMlDirections(directions), [directions])
  const consensus = useMemo(() => consensusMlDirection(counts), [counts])
  const n = counts.up + counts.down + counts.neutral

  const pieData = useMemo(() => {
    const out: { name: string; value: number; fill: string }[] = []
    if (counts.up > 0) out.push({ name: 'Up', value: counts.up, fill: '#198754' })
    if (counts.down > 0) out.push({ name: 'Down', value: counts.down, fill: '#dc3545' })
    if (counts.neutral > 0) out.push({ name: 'Neutral', value: counts.neutral, fill: '#6c757d' })
    return out
  }, [counts])

  return (
    <Card className="border-secondary mb-3">
      <Card.Body className="py-3">
        <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2">
          <div className="small text-muted text-uppercase mb-0" style={{ fontSize: '0.68rem' }}>
            Auto predictions — direction vote
          </div>
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            className="text-nowrap"
            aria-expanded={pieExpanded}
            aria-controls={directionPieCollapseId}
            onClick={() => setPieExpanded((v) => !v)}
          >
            {pieExpanded ? 'Minimise chart' : 'Expand chart'}
          </Button>
        </div>
        {pieExpanded ? null : n === 0 ? (
          <p className="small text-secondary mb-0" style={{ maxWidth: '44rem' }}>
            {totalLoaded === 0
              ? 'No automation rows yet — enable server auto ML for favorites and wait for scheduled runs.'
              : 'No rows match the current filters — widen filters or clear the search box.'}
          </p>
        ) : (
          <div
            className={`small font-monospace fw-semibold mb-0 ${
              consensus === 'up'
                ? 'text-success'
                : consensus === 'down'
                  ? 'text-danger'
                  : 'text-secondary'
            }`}
          >
            Consensus: {consensus != null ? consensus.toUpperCase() : '—'}
            <span className="text-muted fw-normal ms-2">
              ({n} row{n === 1 ? '' : 's'}
              {totalLoaded > 0 && n !== totalLoaded ? ` · ${totalLoaded} loaded before filters` : ''}) — chart minimised
            </span>
          </div>
        )}
        <Collapse in={pieExpanded} mountOnEnter unmountOnExit>
          <div id={directionPieCollapseId}>
            <p className="small text-secondary mb-2 mt-2" style={{ maxWidth: '44rem' }}>
              Built from <strong>recent automation predictions</strong> currently shown in the table below (respects search,
              direction / outcome / interval / engine filters). One count per row. Each row states the{' '}
              <strong>ref close</strong> (price at the reference bar when the prediction was made) and, once scored, the{' '}
              <strong>next close</strong> (price at the bar used to judge the outcome). Plurality sets consensus; ties resolve
              to <strong>neutral</strong>.
            </p>
            {n === 0 ? (
              <div
                className="d-flex align-items-center justify-content-center border border-secondary rounded bg-body-secondary text-secondary small text-center px-2"
                style={{ height: ML_AUTOMATION_DIRECTION_VOTE_PIE_HEIGHT }}
              >
                {totalLoaded === 0
                  ? 'No automation rows yet — enable server auto ML for favorites and wait for scheduled runs.'
                  : 'No rows match the current filters — widen filters or clear the search box.'}
              </div>
            ) : (
              <>
                <div
                  className={`small font-monospace fw-semibold mb-2 ${
                    consensus === 'up'
                      ? 'text-success'
                      : consensus === 'down'
                        ? 'text-danger'
                        : 'text-secondary'
                  }`}
                >
                  Consensus: {consensus != null ? consensus.toUpperCase() : '—'}
                  <span className="text-muted fw-normal ms-2">
                    ({n} row{n === 1 ? '' : 's'}
                    {totalLoaded > 0 && n !== totalLoaded ? ` · ${totalLoaded} loaded before filters` : ''})
                  </span>
                </div>
                <div className="overflow-visible" style={{ height: ML_AUTOMATION_DIRECTION_VOTE_PIE_HEIGHT }}>
                  <ChartWithRightGutter>
                    <ResponsiveContainer width="100%" height="100%" debounce={50}>
                    <PieChart margin={{ top: 10, left: 12, right: 12, bottom: 44 }}>
                      <Pie
                        data={pieData}
                        dataKey="value"
                        nameKey="name"
                        cx="50%"
                        cy="44%"
                        innerRadius={0}
                        outerRadius="62%"
                        paddingAngle={pieData.length > 1 ? 1.5 : 0}
                      >
                        {pieData.map((d) => (
                          <Cell key={d.name} fill={d.fill} />
                        ))}
                      </Pie>
                      <Tooltip formatter={(value: number, name: string) => [`${value} row(s)`, name]} />
                      <Legend
                        verticalAlign="bottom"
                        align="center"
                        layout="horizontal"
                        wrapperStyle={{ fontSize: 12, paddingTop: 4 }}
                      />
                    </PieChart>
                  </ResponsiveContainer>
                  </ChartWithRightGutter>
                </div>
              </>
            )}
          </div>
        </Collapse>
      </Card.Body>
    </Card>
  )
}

/** Fullscreen: one pie per registered model (API order), merging classic + LightGBM history rows. */
function MlFullscreenAllModelsPies({
  priceModels,
  history,
  lightGbmHistory,
  compact,
}: {
  priceModels: PriceDirectionModelsApiResponse | null
  history: readonly MlPredictionLogEntry[]
  lightGbmHistory: readonly MlPredictionLogEntry[]
  compact?: boolean
}) {
  const [piesExpanded, setPiesExpanded] = useState(false)
  const fullscreenPiesCollapseId = useId()
  const modelIds = useMemo(
    () => orderedModelIdsForFullscreenPies(priceModels, history, lightGbmHistory),
    [priceModels, history, lightGbmHistory],
  )

  const descById = useMemo(() => {
    const m = new Map<string, string>()
    for (const x of priceModels?.models ?? []) {
      const id = x.id?.trim()
      if (id) m.set(id, x.description)
    }
    return m
  }, [priceModels])

  const h = compact ? ML_FULLSCREEN_PIE_HEIGHT.compact : ML_FULLSCREEN_PIE_HEIGHT.default

  if (modelIds.length === 0) return null

  return (
    <div className="flex-shrink-0 overflow-visible mb-4">
      <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2">
        <div className="small text-muted text-uppercase mb-0" style={{ fontSize: compact ? '0.62rem' : '0.68rem' }}>
          Prediction outcomes — all models ({modelIds.length})
        </div>
        <Button
          type="button"
          variant="outline-secondary"
          size="sm"
          className="text-nowrap py-0 px-2"
          aria-expanded={piesExpanded}
          aria-controls={fullscreenPiesCollapseId}
          onClick={() => setPiesExpanded((v) => !v)}
        >
          {piesExpanded ? 'Minimise pies' : 'Expand pies'}
        </Button>
      </div>
      {!piesExpanded ? (
        <p className="small text-secondary mb-0" style={{ fontSize: compact ? '0.72rem' : undefined }}>
          Charts minimised — use <strong>Expand pies</strong> to show prediction outcomes per model.
        </p>
      ) : null}
      <Collapse in={piesExpanded} mountOnEnter unmountOnExit>
        <div
          id={fullscreenPiesCollapseId}
          className="d-grid gap-4"
          style={{
            gridTemplateColumns: `repeat(auto-fill, minmax(min(100%, ${ML_FULLSCREEN_PIE_MIN_COL_PX}px), 1fr))`,
          }}
        >
        {modelIds.map((modelId) => {
          const counts = mergedOutcomeCountsForModel(history, lightGbmHistory, modelId)
          const n = counts.correct + counts.wrong + counts.pending
          const desc = descById.get(modelId)
          return (
            <div key={modelId} className="flex-shrink-0 overflow-visible border border-secondary rounded p-3 bg-body-tertiary">
              <div
                className="small font-monospace text-secondary mb-1 fw-semibold"
                style={{
                  wordBreak: 'break-all',
                  lineHeight: 1.3,
                }}
                title={`${modelId} — ${n} prediction row(s)`}
              >
                {modelId}
                <span className="text-muted fw-normal ms-1">({n})</span>
              </div>
              {desc ? (
                <div
                  className="text-muted mb-3"
                  style={{ fontSize: compact ? '0.72rem' : '0.78rem', lineHeight: 1.35 }}
                >
                  {desc}
                </div>
              ) : null}
              <MlOutcomePieChart counts={counts} height={h} />
            </div>
          )
        })}
        </div>
      </Collapse>
    </div>
  )
}

function MlAutomationRecentRowMatchesFilter(
  r: MlAutomationRecentRow,
  query: string,
  favoritesByToken?: ReadonlyMap<string, KiteInstrumentRow>,
): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  const sym = r.tradingsymbol ?? ''
  const exch = r.exchange ?? ''
  const fav = favoritesByToken?.get(r.instrumentToken)
  const favSym = fav?.tradingsymbol ?? ''
  const favExch = fav?.exchange ?? ''
  const category = automationRowCategory(r, favoritesByToken).label
  const chunks = [
    r.id,
    r.instrumentToken,
    sym,
    exch,
    favSym,
    favExch,
    `${sym}${exch ? ` (${exch})` : ''}`,
    favSym && favExch ? `${favSym} (${favExch})` : favSym,
    category,
    r.engineModelId,
    r.interval,
    r.direction,
    String(r.confidence),
    r.outcome,
    formatLocalDateTime(r.predictedAt),
    formatLocalDateTime(r.refBarTime),
    r.nextBarTime ? formatLocalDateTime(r.nextBarTime) : '',
    String(r.refClose),
    r.nextClose != null ? String(r.nextClose) : '',
  ]
  return chunks.some((c) => String(c).toLowerCase().includes(q))
}

/** Prefer API-provided symbol; else resolve from cached favorites so All favorites reflects every contract row. */
function formatMlAutomationSymbol(
  r: MlAutomationRecentRow,
  favoritesByToken?: ReadonlyMap<string, KiteInstrumentRow>,
): string {
  if (r.tradingsymbol?.trim()) {
    return r.exchange?.trim()
      ? `${r.tradingsymbol.trim()} (${r.exchange.trim()})`
      : r.tradingsymbol.trim()
  }
  const fav = favoritesByToken?.get(r.instrumentToken)
  if (fav) return `${fav.tradingsymbol} (${fav.exchange})`
  return r.instrumentToken
}

/**
 * Map a Kite exchange code to the Browse-tab category label so the auto-predictions table can show e.g.
 * <c>F&amp;O</c> for <c>NFO</c>/<c>BFO</c>, <c>Commodities</c> for <c>MCX</c>, <c>Spot</c> for <c>NSE</c>/<c>BSE</c> equity / indices.
 * Falls back to the raw exchange (or <c>'—'</c>) when nothing else fits.
 */
function automationRowCategory(
  r: MlAutomationRecentRow,
  favoritesByToken?: ReadonlyMap<string, KiteInstrumentRow>,
): { label: string; exchange: string } {
  const exch =
    r.exchange?.trim() ||
    favoritesByToken?.get(r.instrumentToken)?.exchange?.trim() ||
    ''
  const upper = exch.toUpperCase()
  if (upper === 'NFO' || upper === 'BFO') return { label: 'F&O', exchange: exch }
  if (upper === 'MCX') return { label: 'Commodities', exchange: exch }
  if (upper === 'NSE' || upper === 'BSE' || upper.endsWith('_INDEX'))
    return { label: 'Spot', exchange: exch }
  return { label: exch || '—', exchange: exch }
}

type AutomationRecentSortColumn =
  | 'predictedAt'
  | 'symbol'
  | 'category'
  | 'engineModelId'
  | 'interval'
  | 'refClose'
  | 'nextClose'
  | 'direction'
  | 'confidence'
  | 'outcome'

/** Low→high uses ascending numeric / oldest-first time / A→Z strings; reversed when <paramref name="highFirst"/>. Null next closes sort last (stable). */
function compareAutomationRecentRows(
  a: MlAutomationRecentRow,
  b: MlAutomationRecentRow,
  col: AutomationRecentSortColumn,
  highFirst: boolean,
  favoritesByToken?: ReadonlyMap<string, KiteInstrumentRow>,
): number {
  const dir = highFirst ? -1 : 1
  const directionRank = (d: string) =>
    d === 'down' ? 0 : d === 'neutral' ? 1 : 2
  const outcomeRank = (o: string) =>
    o === 'pending' ? 0 : o === 'wrong' ? 1 : 2

  switch (col) {
    case 'predictedAt': {
      const ta = Date.parse(a.predictedAt)
      const tb = Date.parse(b.predictedAt)
      const va = Number.isFinite(ta) ? ta : 0
      const vb = Number.isFinite(tb) ? tb : 0
      if (va === vb) return 0
      return va > vb ? dir : -dir
    }
    case 'symbol': {
      const sa = formatMlAutomationSymbol(a, favoritesByToken).toLowerCase()
      const sb = formatMlAutomationSymbol(b, favoritesByToken).toLowerCase()
      const c = sa.localeCompare(sb, undefined, { sensitivity: 'base' })
      if (c === 0) return 0
      return c > 0 ? dir : -dir
    }
    case 'category': {
      const ca = automationRowCategory(a, favoritesByToken).label.toLowerCase()
      const cb = automationRowCategory(b, favoritesByToken).label.toLowerCase()
      const c = ca.localeCompare(cb, undefined, { sensitivity: 'base' })
      if (c === 0) return 0
      return c > 0 ? dir : -dir
    }
    case 'engineModelId': {
      const ea = (a.engineModelId ?? '').toLowerCase()
      const eb = (b.engineModelId ?? '').toLowerCase()
      const c = ea.localeCompare(eb, undefined, { sensitivity: 'base' })
      if (c === 0) return 0
      return c > 0 ? dir : -dir
    }
    case 'interval': {
      const order = CHART_INTERVALS as readonly string[]
      const ia = order.indexOf(a.interval)
      const ib = order.indexOf(b.interval)
      const fa = ia === -1 ? Number.MAX_SAFE_INTEGER : ia
      const fb = ib === -1 ? Number.MAX_SAFE_INTEGER : ib
      if (fa === fb) {
        const t = a.interval.localeCompare(b.interval)
        if (t === 0) return 0
        return t > 0 ? dir : -dir
      }
      return fa > fb ? dir : -dir
    }
    case 'refClose': {
      const va = Number.isFinite(a.refClose) ? a.refClose : 0
      const vb = Number.isFinite(b.refClose) ? b.refClose : 0
      if (va === vb) return 0
      return va > vb ? dir : -dir
    }
    case 'nextClose': {
      const na = a.nextClose != null && Number.isFinite(a.nextClose) ? a.nextClose : null
      const nb = b.nextClose != null && Number.isFinite(b.nextClose) ? b.nextClose : null
      if (na == null && nb == null) return 0
      if (na == null) return 1
      if (nb == null) return -1
      if (na === nb) return 0
      return na > nb ? dir : -dir
    }
    case 'direction': {
      const ra = directionRank(a.direction)
      const rb = directionRank(b.direction)
      if (ra === rb) return 0
      return ra > rb ? dir : -dir
    }
    case 'confidence': {
      if (a.confidence === b.confidence) return 0
      return a.confidence > b.confidence ? dir : -dir
    }
    case 'outcome': {
      const ra = outcomeRank(a.outcome)
      const rb = outcomeRank(b.outcome)
      if (ra === rb) return 0
      return ra > rb ? dir : -dir
    }
    default:
      return 0
  }
}

function sortColumnHeaderClick(
  col: AutomationRecentSortColumn,
  current: AutomationRecentSortColumn,
  setCol: (c: AutomationRecentSortColumn) => void,
  setHighFirst: (v: boolean | ((p: boolean) => boolean)) => void,
): void {
  if (current === col) setHighFirst((h) => !h)
  else {
    setCol(col)
    setHighFirst(
      col === 'predictedAt' ||
        col === 'refClose' ||
        col === 'nextClose' ||
        col === 'confidence',
    )
  }
}

/** Case-insensitive match on model id, outcomes, timestamps, numeric fields, detail. */
function predictionHistoryMatchesFilter(e: MlPredictionLogEntry, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  const chunks = [
    e.id,
    e.modelId,
    e.engineModelId ?? '',
    e.direction,
    e.outcome,
    e.detail,
    String(e.confidence),
    formatLocalDateTime(e.predictedAt),
    formatLocalDateTime(e.refBarTime),
    e.nextBarTime ? formatLocalDateTime(e.nextBarTime) : '',
    String(e.refClose),
    e.nextClose != null ? String(e.nextClose) : '',
  ]
  return chunks.some((c) => String(c).toLowerCase().includes(q))
}

function MlPredictionHistoryTableBody({
  rows,
  compact,
  headActions,
  emptyFilterMessage,
}: {
  rows: MlPredictionLogEntry[]
  compact?: boolean
  /** Rightmost header cell (e.g. refresh history + full screen). */
  headActions?: ReactNode
  /** When <paramref name="rows"/> is empty, one full-width row (e.g. filter matched nothing). */
  emptyFilterMessage?: string
}) {
  const colCount = 11 + (headActions != null ? 1 : 0)
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
          {headActions != null ? (
            <th className="text-end align-middle" style={{ width: '1%' }}>
              <div className="d-flex justify-content-end gap-1 flex-wrap" onClick={(e) => e.stopPropagation()}>
                {headActions}
              </div>
            </th>
          ) : null}
        </tr>
      </thead>
      <tbody>
        {rows.length === 0 && emptyFilterMessage ? (
          <tr>
            <td colSpan={colCount} className="py-4 px-2 text-secondary small text-center fst-italic">
              {emptyFilterMessage}
            </td>
          </tr>
        ) : (
          rows.map((e, idx) => (
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
            {headActions != null ? <td aria-hidden="true" className="p-0 border-secondary" /> : null}
          </tr>
          ))
        )}
      </tbody>
    </Table>
  )
}

/** Search box + filtered prediction history table (classic or LightGBM panel). */
function MlPredictionHistoryTableWithFilter({
  rows,
  compact,
  headActions,
}: {
  rows: MlPredictionLogEntry[]
  compact?: boolean
  headActions?: ReactNode
}) {
  const [filterQuery, setFilterQuery] = useState('')
  const filteredRows = useMemo(
    () => rows.filter((e) => predictionHistoryMatchesFilter(e, filterQuery)),
    [rows, filterQuery],
  )
  const q = filterQuery.trim()

  return (
    <>
      <div className="px-2 py-2 bg-body-secondary border-bottom border-secondary flex-shrink-0">
        <Form.Control
          size="sm"
          type="search"
          className={compact ? 'py-0' : undefined}
          placeholder="Filter (model id, outcome, dates, confidence, detail…)"
          value={filterQuery}
          onChange={(e) => setFilterQuery(e.target.value)}
          aria-label="Filter prediction history rows"
        />
        {q ? (
          <div className="small text-muted mt-1" style={{ fontSize: compact ? '0.62rem' : '0.7rem' }}>
            Showing {filteredRows.length} of {rows.length}
          </div>
        ) : null}
      </div>
      {rows.length > 0 ? (
        <MlPredictionHistoryTableBody
          rows={filteredRows}
          compact={compact}
          headActions={headActions}
          emptyFilterMessage={
            filteredRows.length === 0 && q ? 'No rows match this filter.' : undefined
          }
        />
      ) : null}
    </>
  )
}

/** ML next-bar direction; classic vs LightGBM rows use separate server tables and history endpoints. */
function MlNextBarBiasBar({
  instrumentToken,
  interval,
  compact,
  candleSeries,
  collapseMlByDefault,
  onPredictionHistoryChange,
}: {
  instrumentToken: string
  interval: ChartInterval
  compact?: boolean
  candleSeries: ChartPointWithMa[]
  /** When true (e.g. All favorites tiles), only a one-line toggle is shown until expanded. */
  collapseMlByDefault?: boolean
  /** Fired when classic or LightGBM history changes (combined), for chart overlays. */
  onPredictionHistoryChange?: (entries: readonly MlPredictionLogEntry[]) => void
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
  const mlBiasSectionId = useId()
  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()
  const useMinimizedMlChrome = Boolean(collapseMlByDefault) && !fullscreenActive
  const [mlSectionOpen, setMlSectionOpen] = useState(() => !Boolean(collapseMlByDefault))

  useEffect(() => {
    if (fullscreenActive) setMlSectionOpen(true)
  }, [fullscreenActive])

  useEffect(() => {
    onPredictionHistoryChange?.([...history, ...lightGbmHistory])
  }, [history, lightGbmHistory, onPredictionHistoryChange])

  const storesPredictionsInLightGbm = useMemo(() => {
    if (selectedPriceModelId)
      return selectedPriceModelId === ML_LIGHTGBM_TRIPLE_BARRIER_MODEL_ID
    return priceModels?.defaultModelId === ML_LIGHTGBM_TRIPLE_BARRIER_MODEL_ID
  }, [selectedPriceModelId, priceModels?.defaultModelId])

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
  const showHistoryDataTables = history.length > 0 || lightGbmHistory.length > 0
  const canFullscreenPredictions =
    history.length > 0 ||
    lightGbmHistory.length > 0 ||
    (priceModels != null && priceModels.models.length > 0)

  const mlHistoryHeadActions = useMemo(
    () => (
      <>
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
        {canFullscreenPredictions ? (
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
      </>
    ),
    [historyLoading, reloadHistory, toggleFullscreen, fullscreenActive, compact, canFullscreenPredictions],
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
      {useMinimizedMlChrome ? (
        <div className={`d-flex flex-wrap align-items-center gap-2 ${gapClass}`}>
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            className="py-0 px-2 d-inline-flex align-items-center gap-2"
            onClick={() => setMlSectionOpen((o) => !o)}
            aria-expanded={mlSectionOpen}
            aria-controls={mlBiasSectionId}
            id={`${mlBiasSectionId}-toggle`}
          >
            <span className="text-secondary" style={{ fontSize: '0.65rem', width: '0.65rem' }}>
              {mlSectionOpen ? '▼' : '▶'}
            </span>
            <span style={{ fontSize: compact ? '0.72rem' : undefined }}>ML prediction</span>
            {mlPred ? (
              <span
                className={`font-monospace fw-semibold ${
                  mlPred.direction === 'up'
                    ? 'text-success'
                    : mlPred.direction === 'down'
                      ? 'text-danger'
                      : 'text-secondary'
                }`}
                style={{ fontSize: compact ? '0.72rem' : undefined }}
              >
                {mlPred.direction.toUpperCase()} · {mlPred.confidence}%
              </span>
            ) : mlLoading ? (
              <Spinner animation="border" size="sm" role="status" />
            ) : (
              <span className="text-muted fw-normal" style={{ fontSize: '0.68rem' }}>
                (collapsed)
              </span>
            )}
          </Button>
          {!mlSectionOpen ? (
            <Button
              type="button"
              variant="outline-info"
              size="sm"
              className="py-0 px-2"
              style={{ fontSize: compact ? '0.72rem' : undefined }}
              disabled={mlLoading}
              onClick={() => void fetchMlBias()}
              title="Run ML with the selected default model (expand to change model)"
            >
              {mlLoading ? (
                <>
                  <Spinner animation="border" size="sm" className="me-1" />
                  ML…
                </>
              ) : (
                'Run ML'
              )}
            </Button>
          ) : null}
        </div>
      ) : null}
      {mlError ? (
        <Alert variant="warning" className={`py-1 small ${gapClass}`}>
          {mlError}
        </Alert>
      ) : null}
      <Collapse in={!useMinimizedMlChrome || mlSectionOpen}>
        <div id={mlBiasSectionId}>
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
            {!showHistoryDataTables ? (
              <>
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
                {canFullscreenPredictions ? (
                  <Button
                    type="button"
                    variant="outline-secondary"
                    size="sm"
                    className="py-0 px-2"
                    onClick={() => void toggleFullscreen()}
                    title={fullscreenActive ? 'Exit full screen' : 'Full screen predictions and chart'}
                    aria-label={fullscreenActive ? 'Exit full screen' : 'Full screen predictions'}
                  >
                    {fullscreenActive
                      ? compact
                        ? 'Exit'
                        : 'Exit full screen'
                      : compact
                        ? 'Full'
                        : 'Full predictions'}
                  </Button>
                ) : null}
              </>
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
          {mlPred ? (
            <p
              className={`small text-muted ${fullscreenActive ? 'mb-2' : gapClass}`}
              style={{ fontSize: compact ? '0.72rem' : '0.75rem' }}
            >
              {mlPred.detail}
            </p>
          ) : null}
          <div className="flex-shrink-0 overflow-visible mb-2">
            <MlFullscreenAllModelsPies
              priceModels={priceModels}
              history={history}
              lightGbmHistory={lightGbmHistory}
              compact={compact}
            />
          </div>
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
              <MlPredictionHistoryTableWithFilter
                rows={mlHistoryTableRows}
                compact={compact}
                headActions={mlHistoryHeadActions}
              />
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
              <MlPredictionHistoryTableWithFilter
                rows={mlLightGbmHistoryTableRows}
                compact={compact}
                headActions={mlHistoryHeadActions}
              />
            </div>
          ) : null}
        </div>
      </Collapse>
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
  chartZoomStored,
  onChartZoomStoredChange,
  demoPaperBuyMarkers,
  paperLastBuyPrice,
  zerodhaConnected,
  tileChartControlsSlot,
}: {
  row: KiteInstrumentRow
  rangePreset: ChartRangePreset
  interval: ChartInterval
  graphType: ChartGraphType
  heightPx: number
  maLineVisibility: MaLineVisibility
  customEmaPeriod: number
  chartZoomStored: number | null
  onChartZoomStoredChange: (stored: number | null) => void
  /** OPEN demo BUY legs — vertical markers removed FIFO when sells execute. */
  demoPaperBuyMarkers?: readonly DemoPaperOpenBuyMarkerDto[]
  /** Latest demo BUY fill for an open paper long — horizontal guide. */
  paperLastBuyPrice?: number | null
  zerodhaConnected: boolean
  /** Per-tile chrome inside the fullscreen element (interval override, etc.). */
  tileChartControlsSlot?: ReactNode
}) {
  const [series, setSeries] = useState<ChartPointWithMa[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)
  const [chartRefreshTick, setChartRefreshTick] = useState(0)
  const [mlPredictionOverlayEntries, setMlPredictionOverlayEntries] = useState<readonly MlPredictionLogEntry[]>([])
  const [chartPanOffsetBars, setChartPanOffsetBars] = useState(0)
  const [priceVerticalZoomScale, setPriceVerticalZoomScale] = useState(1)
  const fetchCtxRef = useRef<{
    token: string | null
    interval: ChartInterval | null
    range: ChartRangePreset | null
  }>({ token: null, interval: null, range: null })
  const seriesSourceRef = useRef<ChartPointWithMa[] | null>(null)
  const liveVolAccRef = useRef<LiveTickVolumeAccumulator>({ lastCumulativeVolume: null })

  const live = useLiveMarketTick(row.instrumentToken, zerodhaConnected)

  useEffect(() => {
    const ac = new AbortController()
    const token = row.instrumentToken

    const prev = fetchCtxRef.current
    const contextChanged =
      prev.token !== token || prev.interval !== interval || prev.range !== rangePreset

    fetchCtxRef.current = { token, interval, range: rangePreset }

    if (contextChanged) {
      setSeries([])
      setCandleRange(null)
      setError(null)
    }

    setLoading(true)

    const fetchOnce = async (initial: boolean) => {
      try {
        const data = await fetchMergedHistoricalChartCandles(
          row.instrumentToken,
          interval,
          historicalRangeQueryParams(rangePreset),
          ac.signal,
        )
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

  useEffect(() => {
    setMlPredictionOverlayEntries([])
  }, [row.instrumentToken, interval])

  useEffect(() => {
    setPriceVerticalZoomScale(1)
  }, [row.instrumentToken])

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
  }, [chartZoomStored, row.instrumentToken])

  useEffect(() => {
    setChartPanOffsetBars((p) =>
      clampChartPanAllowNewerGhost(p, seriesWithCustom.length, zoomVisibleBars),
    )
  }, [seriesWithCustom.length, zoomVisibleBars])

  const chartPanEnabled =
    zoomVisibleBars != null &&
    seriesWithCustom.length > zoomVisibleBars &&
    seriesWithCustom.length > 1

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
    () =>
      [...chartDataIndicesForPaperBuyMarkers(demoPaperBuyMarkers ?? [], chartData, interval as ChartIntervalKey)],
    [demoPaperBuyMarkers, chartData, interval],
  )

  const paperBuyReferenceLines = useMemo(() => {
    return paperBuyDataIndices.map((di, seg) => {
      const rowPt = chartData[di]
      if (!rowPt) return null
      return (
        <ReferenceLine
          key={`demo-pbuy-${row.instrumentToken}-${seg}-${rowPt.t}`}
          x={rowPt.idx}
          stroke="#84cc16"
          strokeWidth={1.35}
          strokeDasharray="5 5"
          opacity={0.92}
        />
      )
    })
  }, [paperBuyDataIndices, chartData, row.instrumentToken])

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

  const compactChartZoomToolbar = (
    <ChartZoomControls
      idPrefix={`fav-chart-${row.instrumentToken}`}
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
      chartRefreshing={loading}
    />
  )

  const compactHasChart = !error && series.length > 0
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
          {candleRange && !error ? (
            <HistoricalRangeCaption
              compact
              candleInterval={candleRange.interval}
              fromIso={candleRange.from}
              toIso={candleRange.to}
            />
          ) : null}
          <MlNextBarBiasBar
            instrumentToken={row.instrumentToken}
            interval={interval}
            compact
            collapseMlByDefault
            candleSeries={seriesWithCustom}
            onPredictionHistoryChange={setMlPredictionOverlayEntries}
          />
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
              {tileChartControlsSlot}
              {candleRange && !error ? (
                <HistoricalRangeCaption
                  compact
                  candleInterval={candleRange.interval}
                  fromIso={candleRange.from}
                  toIso={candleRange.to}
                />
              ) : null}
              <MlNextBarBiasBar
                instrumentToken={row.instrumentToken}
                interval={interval}
                compact
                collapseMlByDefault
                candleSeries={seriesWithCustom}
                onPredictionHistoryChange={setMlPredictionOverlayEntries}
              />
              {compactChartZoomToolbar}
            </div>
          ) : null}
          {!fullscreenActive ? (
            <>
              {tileChartControlsSlot ? <div className="mb-2">{tileChartControlsSlot}</div> : null}
              {compactChartZoomToolbar}
            </>
          ) : null}
          <div
            className={fullscreenActive ? 'flex-grow-1 w-100' : 'w-100'}
            style={{
              height: fullscreenActive ? '100%' : heightPx,
              flex: fullscreenActive ? '1 1 auto' : undefined,
              minHeight: fullscreenActive ? 0 : undefined,
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
              mlPredictionEntries={mlPredictionOverlayEntries}
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

const FAVORITE_TILE_AUTOMATION_ROWS_MAX = 8

/** Per-tile strip: latest automation ML rows for one instrument (same payload as Auto predictions tab). */
function FavoriteTileAutomationMlPanel({
  instrumentToken,
  automationRecent,
  automationRecentLoading,
  automationPriceModels,
}: {
  instrumentToken: string
  automationRecent: readonly MlAutomationRecentRow[]
  automationRecentLoading: boolean
  automationPriceModels: PriceDirectionModelsApiResponse | null
}) {
  const [automationPanelOpen, setAutomationPanelOpen] = useState(false)
  const rowsForSymbol = useMemo(() => {
    const t = instrumentToken.trim()
    return sortByPredictedAtNewestFirst(
      automationRecent.filter((r) => r.instrumentToken.trim() === t),
    ).slice(0, FAVORITE_TILE_AUTOMATION_ROWS_MAX)
  }, [instrumentToken, automationRecent])

  const descByEngineId = useMemo(() => {
    const m = new Map<string, string>()
    for (const x of automationPriceModels?.models ?? []) {
      const id = x.id?.trim()
      if (id) m.set(id, x.description?.trim() ?? '')
    }
    return m
  }, [automationPriceModels])

  return (
    <div className="mt-2 rounded-3 border border-secondary-subtle overflow-hidden shadow-sm">
      <button
        type="button"
        className="w-100 text-start px-2 py-1 border-0 bg-body-secondary border-bottom border-secondary-subtle d-flex align-items-center justify-content-between gap-2"
        onClick={() => setAutomationPanelOpen((o) => !o)}
        aria-expanded={automationPanelOpen}
      >
        <span className="small text-secondary text-uppercase mb-0" style={{ fontSize: '0.65rem' }}>
          Auto ML predictions
        </span>
        <span className="d-flex align-items-center gap-2 flex-shrink-0">
          {automationRecentLoading && rowsForSymbol.length === 0 ? (
            <Spinner animation="border" size="sm" role="status" />
          ) : (
            <span className="text-muted font-monospace" style={{ fontSize: '0.65rem' }}>
              {rowsForSymbol.length} row{rowsForSymbol.length === 1 ? '' : 's'}
            </span>
          )}
          <span className="text-secondary user-select-none" style={{ fontSize: '0.65rem' }}>
            {automationPanelOpen ? '▼' : '▶'}
          </span>
        </span>
      </button>
      <Collapse in={automationPanelOpen}>
        <div>
          <div className="p-2 bg-body-tertiary bg-opacity-25">
            <p className="text-muted mb-2" style={{ fontSize: '0.62rem' }}>
              Same time range as the <strong>Auto predictions</strong> tab (adjust there to change this list).
            </p>
            {automationRecentLoading && rowsForSymbol.length === 0 ? (
              <div className="d-flex align-items-center gap-2 text-muted" style={{ fontSize: '0.7rem' }}>
                <Spinner animation="border" size="sm" role="status" />
                Loading…
              </div>
            ) : rowsForSymbol.length === 0 ? (
              <p className="text-muted mb-0 fst-italic" style={{ fontSize: '0.68rem' }}>
                No automation rows in range for this symbol yet.
              </p>
            ) : (
              <div className="table-responsive" style={{ maxHeight: '200px', overflowY: 'auto' }}>
                <Table striped bordered size="sm" className="mb-0 align-middle font-monospace" style={{ fontSize: '0.63rem' }}>
                  <thead className="table-light text-nowrap">
                    <tr>
                      <th>Time</th>
                      <th>Iv</th>
                      <th>Engine</th>
                      <th>Dir</th>
                      <th>%</th>
                      <th>Out</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rowsForSymbol.map((r) => {
                      const eng = r.engineModelId?.trim() ?? ''
                      const short = eng.length > 18 ? `${eng.slice(0, 16)}…` : eng
                      const desc = descByEngineId.get(eng)
                      return (
                        <tr key={r.id}>
                          <td className="text-nowrap">{formatLocalDateTime(r.predictedAt)}</td>
                          <td>{r.interval}</td>
                          <td className="text-truncate" style={{ maxWidth: '5.5rem' }} title={desc ? `${eng} — ${desc}` : eng || '—'}>
                            {short || '—'}
                          </td>
                          <td>{r.direction}</td>
                          <td>{r.confidence}%</td>
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
                      )
                    })}
                  </tbody>
                </Table>
              </div>
            )}
          </div>
        </div>
      </Collapse>
    </div>
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
  trendAnalysisSelections,
  onTrendAnalysisSelectionsChange,
  listTilePrimaryAction,
  listTilePrimaryLabel,
  tradingLockKeySet,
  onToggleTradingLock,
  chartZoomByInstrumentToken,
  onInstrumentChartZoomChange,
  automationRecent,
  automationRecentLoading,
  automationPriceModels,
  zerodhaConnected,
  demoPaperOpenBuysByInstrumentToken,
  demoPaperLastBuyPriceByInstrumentToken,
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
  trendAnalysisSelections: ChartInterval[]
  onTrendAnalysisSelectionsChange: (next: ChartInterval[]) => void
  listTilePrimaryAction: (r: KiteInstrumentRow) => void
  listTilePrimaryLabel: string
  tradingLockKeySet?: Set<string>
  onToggleTradingLock?: (r: KiteInstrumentRow) => void
  chartZoomByInstrumentToken: Record<string, number>
  onInstrumentChartZoomChange: (instrumentToken: string, chartZoomStored: number | null) => void
  automationRecent: readonly MlAutomationRecentRow[]
  automationRecentLoading: boolean
  automationPriceModels: PriceDirectionModelsApiResponse | null
  zerodhaConnected: boolean
  demoPaperOpenBuysByInstrumentToken: Readonly<Record<string, DemoPaperOpenBuyMarkerDto[]>>
  demoPaperLastBuyPriceByInstrumentToken: Readonly<Record<string, number>>
}) {
  const favHistExtras = useMemo(() => historicalRangeQueryParams(rangePreset), [rangePreset])
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
        trendAnalysisSelections={trendAnalysisSelections}
        onTrendAnalysisSelectionsChange={onTrendAnalysisSelectionsChange}
        graphType={graphType}
        onGraphTypeChange={onGraphTypeChange}
        maLineVisibility={maLineVisibility}
        onMaLineVisibilityChange={onMaLineVisibilityChange}
        customEmaPeriod={customEmaPeriod}
        onCustomEmaPeriodChange={onCustomEmaPeriodChange}
        trendPresetHint="Multi-select does not change the chart interval row; each tile shows multi-interval trend below the chart"
      />
      <p className="small text-secondary mb-3" style={{ maxWidth: '48rem' }}>
        <strong>Trend analysis</strong> checkboxes choose which candle sizes participate in{' '}
        <strong>Multi-interval trend</strong> panels (least-squares on past closes — same{' '}
        <strong>Range</strong> as charts). Use <strong>Interval</strong> to set bar size on every tile (clears symbol
        drops).         Per-user <strong>Auto ML bar interval</strong> lives on the <strong>Auto predictions</strong> tab; otherwise the
        server may use <span className="font-monospace text-body-secondary">FavoriteMlAutomation:PredictionIntervalOverride</span>{' '}
        or your chart interval.
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
                  <div className="d-flex flex-wrap gap-1 justify-content-end">
                    <Button
                      type="button"
                      variant="outline-warning"
                      size="sm"
                      className="py-0 px-2 text-nowrap"
                      onClick={() => listTilePrimaryAction(row)}
                    >
                      {listTilePrimaryLabel}
                    </Button>
                    {onToggleTradingLock && tradingLockKeySet ? (
                      <Button
                        type="button"
                        variant={
                          tradingLockKeySet.has(favoriteRowKey(row)) ? 'outline-info' : 'outline-secondary'
                        }
                        size="sm"
                        className="py-0 px-2 text-nowrap"
                        aria-pressed={tradingLockKeySet.has(favoriteRowKey(row))}
                        onClick={() => onToggleTradingLock(row)}
                      >
                        {tradingLockKeySet.has(favoriteRowKey(row)) ? '🔒 Locked' : '🔓 Lock'}
                      </Button>
                    ) : null}
                  </div>
                </div>
                <CompactPriceChart
                  row={row}
                  rangePreset={rangePreset}
                  interval={chartIntervalByInstrumentToken[row.instrumentToken] ?? defaultInterval}
                  graphType={graphType}
                  heightPx={220}
                  maLineVisibility={maLineVisibility}
                  customEmaPeriod={customEmaPeriod}
                  chartZoomStored={chartZoomByInstrumentToken[row.instrumentToken] ?? null}
                  onChartZoomStoredChange={(stored) => onInstrumentChartZoomChange(row.instrumentToken, stored)}
                  demoPaperBuyMarkers={demoPaperOpenBuysByInstrumentToken[row.instrumentToken]}
                  paperLastBuyPrice={demoPaperLastBuyPriceByInstrumentToken[row.instrumentToken] ?? null}
                  zerodhaConnected={zerodhaConnected}
                  tileChartControlsSlot={
                    <Form.Group className="mb-0" controlId={`fav-iv-${row.instrumentToken}`}>
                      <Form.Label className="small text-secondary text-uppercase mb-1">
                        Candles for this symbol
                      </Form.Label>
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
                  }
                />
                <TrendAnalysisMultiPanel
                  instrumentToken={row.instrumentToken}
                  symbolLabel={row.tradingsymbol}
                  historicalQueryExtra={favHistExtras}
                  selectedIntervalsOrdered={orderTrendSelections(trendAnalysisSelections)}
                  variant="favoriteLazy"
                />
                {zerodhaConnected ? (
                  <FavoriteTileAutomationMlPanel
                    instrumentToken={row.instrumentToken}
                    automationRecent={automationRecent}
                    automationRecentLoading={automationRecentLoading}
                    automationPriceModels={automationPriceModels}
                  />
                ) : null}
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
  isTradingLocked,
  onToggleTradingLock,
  liveLastPrice,
  liveLastTick,
  chartZoomStored,
  onChartZoomStoredChange,
  trendAnalysisSelections,
  onTrendAnalysisSelectionsChange,
  demoPaperBuyMarkers,
  paperLastBuyPrice,
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
  isTradingLocked?: boolean
  onToggleTradingLock?: () => void
  liveLastPrice?: number | null
  liveLastTick?: MarketTickBatchItem | null
  chartZoomStored: number | null
  onChartZoomStoredChange: (stored: number | null) => void
  trendAnalysisSelections: ChartInterval[]
  onTrendAnalysisSelectionsChange: (next: ChartInterval[]) => void
  demoPaperBuyMarkers?: readonly DemoPaperOpenBuyMarkerDto[]
  paperLastBuyPrice?: number | null
}) {
  const browseHistExtras = useMemo(() => historicalRangeQueryParams(rangePreset), [rangePreset])
  const [series, setSeries] = useState<ChartPointWithMa[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [candleRange, setCandleRange] = useState<CandleRangeMeta | null>(null)
  const [chartRefreshTick, setChartRefreshTick] = useState(0)
  const [mlPredictionOverlayEntries, setMlPredictionOverlayEntries] = useState<readonly MlPredictionLogEntry[]>([])
  const [chartPanOffsetBars, setChartPanOffsetBars] = useState(0)
  const [priceVerticalZoomScale, setPriceVerticalZoomScale] = useState(1)
  const chartFetchCtxRef = useRef<{
    token: string | null
    interval: ChartInterval | null
    range: ChartRangePreset | null
  }>({ token: null, interval: null, range: null })
  const browseSeriesSourceRef = useRef<ChartPointWithMa[] | null>(null)
  const browseLiveVolAccRef = useRef<LiveTickVolumeAccumulator>({ lastCumulativeVolume: null })
  const { panelRef, fullscreenActive, toggleFullscreen } = useChartFullscreen()

  useEffect(() => {
    if (!selection) {
      chartFetchCtxRef.current = { token: null, interval: null, range: null }
      setSeries([])
      setCandleRange(null)
      setError(null)
      setLoading(false)
      return
    }

    const token = selection.instrumentToken
    const ac = new AbortController()

    const prev = chartFetchCtxRef.current
    const contextChanged =
      prev.token !== token || prev.interval !== interval || prev.range !== rangePreset

    chartFetchCtxRef.current = { token, interval, range: rangePreset }

    if (contextChanged) {
      setSeries([])
      setCandleRange(null)
      setError(null)
    }

    setLoading(true)

    const fetchOnce = async (initial: boolean) => {
      try {
        const data = await fetchMergedHistoricalChartCandles(
          token,
          interval,
          historicalRangeQueryParams(rangePreset),
          ac.signal,
        )
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
  }, [selection?.instrumentToken, interval, rangePreset, chartRefreshTick])

  useEffect(() => {
    setMlPredictionOverlayEntries([])
  }, [selection?.instrumentToken, interval])

  useEffect(() => {
    setPriceVerticalZoomScale(1)
  }, [selection?.instrumentToken])

  const displaySeries = useMemo(() => {
    if (browseSeriesSourceRef.current !== series) {
      browseSeriesSourceRef.current = series
      browseLiveVolAccRef.current.lastCumulativeVolume = null
    }
    return mergeLiveTickIntoOhlc(
      series,
      liveLastTick ?? null,
      interval as ChartIntervalKey,
      graphType,
      browseLiveVolAccRef.current,
    )
  }, [series, liveLastTick, interval, graphType])

  const customEmaApplied = useMemo(
    () => effectiveCustomEmaPeriod(maLineVisibility, customEmaPeriod),
    [maLineVisibility, customEmaPeriod],
  )

  const displayWithMa = useMemo(
    () => addCustomEmaToChartPoints(attachMovingAverages(displaySeries), customEmaApplied),
    [displaySeries, customEmaApplied],
  )

  const zoomVisibleBars = useMemo(
    () => visibleBarsFromChartZoomStored(chartZoomStored, displayWithMa.length),
    [chartZoomStored, displayWithMa.length],
  )

  useEffect(() => {
    if (chartZoomStored == null || displayWithMa.length === 0) return
    const next = correctedChartZoomStored(chartZoomStored, displayWithMa.length)
    if (next === undefined) return
    if (next === chartZoomStored) return
    onChartZoomStoredChange(next)
  }, [chartZoomStored, displayWithMa.length, onChartZoomStoredChange])

  useEffect(() => {
    setChartPanOffsetBars(0)
  }, [chartZoomStored, selection?.instrumentToken])

  useEffect(() => {
    setChartPanOffsetBars((p) =>
      clampChartPanAllowNewerGhost(p, displayWithMa.length, zoomVisibleBars),
    )
  }, [displayWithMa.length, zoomVisibleBars])

  const chartPanEnabled =
    zoomVisibleBars != null &&
    displayWithMa.length > zoomVisibleBars &&
    displayWithMa.length > 1

  const { panPointerProps: browsePanPointerProps } = useChartPanPointerHandlers({
    enabled: chartPanEnabled,
    totalBars: displayWithMa.length,
    visibleBarCount: zoomVisibleBars,
    maxNewerGhostBars: zoomVisibleBars ?? 0,
    panOffsetBars: chartPanOffsetBars,
    setPanOffsetBars: setChartPanOffsetBars,
  })

  const { style: browseChartPanPointerStyle, ...browseChartPanPointerHandlers } = browsePanPointerProps

  const chartData = useMemo(
    () => sliceChartForZoom(displayWithMa, zoomVisibleBars, chartPanOffsetBars),
    [displayWithMa, zoomVisibleBars, chartPanOffsetBars],
  )

  const browsePaperBuyDataIndices = useMemo(
    () =>
      [...chartDataIndicesForPaperBuyMarkers(demoPaperBuyMarkers ?? [], chartData, interval as ChartIntervalKey)],
    [demoPaperBuyMarkers, chartData, interval],
  )

  const browsePaperBuyReferenceLines = useMemo(() => {
    const tok = selection?.instrumentToken ?? 'na'
    return browsePaperBuyDataIndices.map((di, seg) => {
      const rowPt = chartData[di]
      if (!rowPt) return null
      return (
        <ReferenceLine
          key={`demo-pbuy-${tok}-${seg}-${rowPt.t}`}
          x={rowPt.idx}
          stroke="#84cc16"
          strokeWidth={1.35}
          strokeDasharray="5 5"
          opacity={0.92}
        />
      )
    })
  }, [browsePaperBuyDataIndices, chartData, selection?.instrumentToken])

  const rechartsYDomain = useMemo(() => {
    let d = yDomainForOhlcAndVisibleMas(chartData, maLineVisibility)
    d = extendYDomainWithLivePrice(d, liveLastPrice)
    d = extendYDomainWithLivePrice(d, paperLastBuyPrice)
    return applyVerticalPriceZoomToDomain(d ?? undefined, priceVerticalZoomScale) ?? d
  }, [chartData, maLineVisibility, liveLastPrice, paperLastBuyPrice, priceVerticalZoomScale])

  const liveLtpReferenceLine =
    liveLastPrice != null && Number.isFinite(liveLastPrice) ? (
      <ReferenceLine
        y={liveLastPrice}
        stroke="#38bdf8"
        strokeWidth={1.65}
        strokeDasharray="6 7"
        label={{ value: 'LTP', position: 'insideRight', fill: '#38bdf8', fontSize: 10, fontWeight: 600 }}
      />
    ) : null

  const paperLastBuyReferenceLine =
    paperLastBuyPrice != null && Number.isFinite(paperLastBuyPrice) ? (
      <ReferenceLine
        y={paperLastBuyPrice}
        stroke="#f59e0b"
        strokeWidth={1.5}
        strokeDasharray="4 6"
        label={{ value: 'Last buy', position: 'insideLeft', fill: '#f59e0b', fontSize: 10, fontWeight: 600 }}
      />
    ) : null

  const onChartZoomIn = useCallback(() => {
    onChartZoomStoredChange(zoomInChartZoomStored(chartZoomStored, displayWithMa.length))
  }, [onChartZoomStoredChange, chartZoomStored, displayWithMa.length])

  const onChartZoomOut = useCallback(() => {
    onChartZoomStoredChange(zoomOutChartZoomStored(chartZoomStored, displayWithMa.length))
  }, [onChartZoomStoredChange, chartZoomStored, displayWithMa.length])

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

  const browseChartZoomToolbar = (
    <ChartZoomControls
      idPrefix="browse-chart-zoom"
      totalBars={displayWithMa.length}
      visibleBarCount={zoomVisibleBars}
      onHorizontalZoomIn={onChartZoomIn}
      onHorizontalZoomOut={onChartZoomOut}
      onHorizontalZoomReset={onChartZoomReset}
      verticalZoomScale={priceVerticalZoomScale}
      onVerticalZoomIn={onVerticalZoomIn}
      onVerticalZoomOut={onVerticalZoomOut}
      onVerticalZoomReset={onVerticalZoomReset}
      onToggleFullscreen={toggleFullscreen}
      fullscreenActive={fullscreenActive}
      onRefreshChart={() => setChartRefreshTick((n) => n + 1)}
      chartRefreshing={loading}
    />
  )

  const browseHasChartData = !error && displayWithMa.length > 0
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
          {onToggleTradingLock ? (
            <Button
              type="button"
              variant={isTradingLocked ? 'outline-info' : 'outline-secondary'}
              size="sm"
              className="py-0 px-2"
              aria-label={isTradingLocked ? 'Unlock for trading' : 'Lock for trading'}
              aria-pressed={Boolean(isTradingLocked)}
              onClick={() => onToggleTradingLock()}
            >
              {isTradingLocked ? '🔒 Locked' : '🔓 Lock trade'}
            </Button>
          ) : null}
        </p>
        {candleRange && !error ? (
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
          trendAnalysisSelections={trendAnalysisSelections}
          onTrendAnalysisSelectionsChange={onTrendAnalysisSelectionsChange}
          graphType={graphType}
          onGraphTypeChange={onGraphTypeChange}
          maLineVisibility={maLineVisibility}
          onMaLineVisibilityChange={onMaLineVisibilityChange}
          customEmaPeriod={customEmaPeriod}
          onCustomEmaPeriodChange={onCustomEmaPeriodChange}
        />
        <TrendAnalysisMultiPanel
          instrumentToken={selection.instrumentToken}
          symbolLabel={`${selection.tradingsymbol} · ${selection.exchange}`}
          historicalQueryExtra={browseHistExtras}
          selectedIntervalsOrdered={orderTrendSelections(trendAnalysisSelections)}
          variant="browseAlways"
        />
        <MlNextBarBiasBar
          instrumentToken={selection.instrumentToken}
          interval={interval}
          candleSeries={displayWithMa}
          onPredictionHistoryChange={setMlPredictionOverlayEntries}
        />
      </>
    )

  return (
    <Card className="border-secondary mt-4">
      <Card.Body>
        <Card.Title className="h6 mb-2">Price chart</Card.Title>
        {!selection ? (
          <p className="text-secondary small mb-0">
            Choose <strong>Candles</strong> for OHLC candlesticks (optional <strong>Trend LR</strong> regression under{' '}
            <strong>Indicators</strong>), or use line/bar views for close-based plots with the same overlays. Charts support{' '}
            <strong>SMA 20</strong>, <strong>EMA 9</strong>, <strong>EMA 21</strong>; live ticks update the current bar in{' '}
            <strong>Candles</strong> while subscribed.
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
                    {browseChartZoomToolbar}
                  </div>
                ) : null}
                {!fullscreenActive ? browseChartZoomToolbar : null}
                <div
                  className={fullscreenActive ? 'flex-grow-1 w-100' : undefined}
                  style={{
                    height: fullscreenActive ? '100%' : '18rem',
                    flex: fullscreenActive ? '1 1 auto' : undefined,
                    minHeight: fullscreenActive ? 0 : undefined,
                    ...browseChartPanPointerStyle,
                  }}
                  {...browseChartPanPointerHandlers}
                >
                  <InstrumentPriceChart
                    graphType={graphType}
                    data={chartData}
                    maLineVisibility={maLineVisibility}
                    customEmaPeriod={customEmaApplied}
                    livePrice={liveLastPrice ?? null}
                    paperLastBuyPrice={paperLastBuyPrice ?? null}
                    paperBuyDataIndices={browsePaperBuyDataIndices}
                    mlPredictionEntries={mlPredictionOverlayEntries}
                    rechartsYDomain={rechartsYDomain ?? undefined}
                    referenceLines={
                      <>
                        {browsePaperBuyReferenceLines}
                        {paperLastBuyReferenceLine}
                        {liveLtpReferenceLine}
                      </>
                    }
                    density="comfortable"
                    newerGhostBars={Math.max(0, -chartPanOffsetBars)}
                    priceVerticalZoomScale={priceVerticalZoomScale}
                  />
                </div>
              </div>
            ) : null}
            <p className="small text-muted mb-2" style={{ fontSize: '0.78rem' }}>
              Historical data refreshes about every {Math.round(CHART_LIVE_POLL_MS / 1000)}s while this tab is visible.
              Charts show <strong>volume</strong> under the price series (candles, line, and bar views) alongside{' '}
              <strong>SMA 20</strong>, <strong>EMA 9</strong>, <strong>EMA 21</strong>, optional S/R bands,
              optional <strong>Trend LR</strong> on <strong>candles</strong> (least-squares regression on close over zoomed bars),
              and an optional custom-period <strong>EMA</strong>. A cyan dashed <strong>LTP</strong> line tracks the streamed last
              price on candles, line, and bar views when subscribed; live ticks also update the in-progress{' '}
              <strong>candle</strong> in Candles view. Lime dashed verticals mark candles where OPEN demo{' '}
              <strong>paper BUY</strong> legs started (FIFO: lines drop as you <strong>sell</strong> lots); an{' '}
              <strong className="text-warning">amber dashed “Last buy”</strong> horizontal locks the latest demo BUY fill when you still hold lots.{' '}
              <strong>ML next-bar bias</strong>{' '}
              shows above bars as a compact row of Font Awesome arrows—green for <strong>up</strong> and red for <strong>down</strong>
              —one icon per model’s next-interval call for <em>that candle&apos;s interval</em>. Hover a candle or use the{' '}
              <strong>ML history</strong> panel for full rows. Calls{' '}
              <span className="font-monospace">/api/v1/predictions/price-direction</span> with an optional{' '}
              <span className="font-monospace">model</span> query (see{' '}
              <span className="font-monospace">/predictions/price-direction/models</span>); not financial advice.
            </p>
            {error ? (
              <Alert variant="danger" className="py-2 small mb-2">
                {error}
              </Alert>
            ) : null}
            {displayWithMa.length === 0 ? (
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
  const automationDirToggleIdPrefix = useId()
  const automationOutcomeToggleIdPrefix = useId()
  const automationEngineToggleIdPrefix = useId()
  const automationIntervalToggleIdPrefix = useId()
  const [favorites, setFavorites] = useState<KiteInstrumentRow[]>([])
  const [favoritesError, setFavoritesError] = useState<string | null>(null)
  const [tradingLocks, setTradingLocks] = useState<KiteInstrumentRow[]>([])
  const [tradingLocksError, setTradingLocksError] = useState<string | null>(null)

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

  const loadTradingLocks = useCallback(async () => {
    try {
      const { data } = await api.get<KiteTradingLocksResponse>('/broker/kite/trading-locks')
      setTradingLocks(data.items)
      setTradingLocksError(null)
    } catch (err) {
      setTradingLocks([])
      setTradingLocksError(problemDetail(err))
    }
  }, [])

  const favoriteKeySet = useMemo(() => new Set(favorites.map(favoriteRowKey)), [favorites])

  const tradingLockKeySet = useMemo(() => new Set(tradingLocks.map(favoriteRowKey)), [tradingLocks])

  /** Favorites + trading locks keyed by instrument token — used when automation rows omit symbol (e.g. MCX crude). */
  const favoriteByInstrumentToken = useMemo(() => {
    const m = new Map<string, KiteInstrumentRow>()
    for (const row of favorites) {
      if (row.instrumentToken) m.set(row.instrumentToken, row)
    }
    for (const row of tradingLocks) {
      const t = row.instrumentToken?.trim()
      if (t && !m.has(t)) m.set(t, row)
    }
    return m
  }, [favorites, tradingLocks])

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

  const toggleTradingLock = useCallback(
    async (r: KiteInstrumentRow) => {
      const key = favoriteRowKey(r)
      const exists = tradingLocks.some((x) => favoriteRowKey(x) === key)
      setTradingLocksError(null)
      try {
        if (exists) {
          await api.delete('/broker/kite/trading-locks', { params: { instrumentToken: r.instrumentToken } })
        } else {
          await api.post('/broker/kite/trading-locks', r)
        }
        await loadTradingLocks()
      } catch (err) {
        setTradingLocksError(problemDetail(err))
      }
    },
    [tradingLocks, loadTradingLocks],
  )

  const [mainTab, setMainTab] = useState<MainTab>(() =>
    typeof window !== 'undefined' ? mainTabFromSearchParams(new URLSearchParams(window.location.search)) : 'browse',
  )

  useEffect(() => {
    setMainTab(mainTabFromSearchParams(searchParams))
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
          } else if (next === 'tradingLocks') {
            p.set('tab', 'locked')
            p.delete('fav')
          } else if (next === 'automation') {
            p.set('tab', 'automation')
            p.delete('fav')
          } else if (next === 'autoTrading') {
            p.set('tab', 'demo-auto-trade')
            p.delete('fav')
          } else if (next === 'manualTrade') {
            p.set('tab', 'manual-trade')
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
  const [todayTopVisibleCount, setTodayTopVisibleCount] = useState(TODAY_TOP_MOVERS_PAGE_SIZE)
  const [todayTopBasis, setTodayTopBasis] = useState('')
  const [todayTopLoading, setTodayTopLoading] = useState(false)
  const [todayTopError, setTodayTopError] = useState<string | null>(null)
  const [chartRow, setChartRow] = useState<KiteInstrumentRow | null>(null)
  const [chartInterval, setChartInterval] = useState<ChartInterval>('5m')
  const [trendAnalysisSelections, setTrendAnalysisSelections] =
    useState<ChartInterval[]>(loadTrendAnalysisSelections)
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

  useEffect(() => {
    try {
      window.localStorage.setItem(TRADER_TREND_ANALYSIS_INTERVALS_LS, JSON.stringify(trendAnalysisSelections))
    } catch {
      /* ignore */
    }
  }, [trendAnalysisSelections])

  const [chartPrefsHydrated, setChartPrefsHydrated] = useState(false)
  const [favoriteMlAutomationEnabled, setFavoriteMlAutomationEnabled] = useState(false)
  const [favoriteMlAutomationBarInterval, setFavoriteMlAutomationBarInterval] = useState('')
  const [favoriteMlAutomationPollInput, setFavoriteMlAutomationPollInput] = useState('')
  const mlAutomationPollTouchedRef = useRef(false)
  const [favoriteMlAutomationMinSecAfterOpenInput, setFavoriteMlAutomationMinSecAfterOpenInput] = useState('')
  const mlAutomationMinSecAfterOpenTouchedRef = useRef(false)
  const [mlAutomationSaving, setMlAutomationSaving] = useState(false)
  const [mlAutomationError, setMlAutomationError] = useState<string | null>(null)
  const [demoAutoTradeEnabled, setDemoAutoTradeEnabled] = useState(false)
  const [demoAutoTradeStrategy, setDemoAutoTradeStrategy] = useState('equal_split')
  const [demoAutoTradeNotionalInr, setDemoAutoTradeNotionalInr] = useState(10_000)
  const [demoAutoTradeSaving, setDemoAutoTradeSaving] = useState(false)
  const [demoEodSummary, setDemoEodSummary] = useState<DemoAutoTradeEodSummaryDto | null>(null)
  const [demoEodLoading, setDemoEodLoading] = useState(false)
  const [demoEodError, setDemoEodError] = useState<string | null>(null)
  const [demoFullReport, setDemoFullReport] = useState<DemoAutoTradeFullReportDto | null>(null)
  const [demoFullReportLoading, setDemoFullReportLoading] = useState(false)
  const [demoFullReportError, setDemoFullReportError] = useState<string | null>(null)
  const [demoTodayLegs, setDemoTodayLegs] = useState<DemoAutoTradeTodayLegsDto | null>(null)
  const [demoTodayLegsLoading, setDemoTodayLegsLoading] = useState(false)
  const [demoTodayLegsError, setDemoTodayLegsError] = useState<string | null>(null)
  const [demoPaperPositions, setDemoPaperPositions] = useState<DemoPaperPositionListItemDto[]>([])
  const [demoPaperPositionsLoading, setDemoPaperPositionsLoading] = useState(false)
  const [demoPaperPositionsError, setDemoPaperPositionsError] = useState<string | null>(null)
  const [demoPaperTrades, setDemoPaperTrades] = useState<DemoPaperTradeHistoryRowDto[]>([])
  const [demoPaperTradesLoading, setDemoPaperTradesLoading] = useState(false)
  const [demoPaperTradesError, setDemoPaperTradesError] = useState<string | null>(null)
  const [demoPaperToken, setDemoPaperToken] = useState('')
  const [demoPaperContracts, setDemoPaperContracts] = useState('1')
  const [demoPaperTradeBusy, setDemoPaperTradeBusy] = useState<{
    side: 'buy' | 'sell'
    instrumentToken: string
  } | null>(null)
  const [demoPaperTradeError, setDemoPaperTradeError] = useState<string | null>(null)
  const [demoPaperTradeLast, setDemoPaperTradeLast] = useState<string | null>(null)
  const demoPaperOpenBuysByInstrumentToken = useMemo(() => {
    const r: Record<string, DemoPaperOpenBuyMarkerDto[]> = {}
    for (const p of demoPaperPositions) {
      if (p.openBuys.length > 0) r[p.instrumentToken] = p.openBuys
    }
    return r
  }, [demoPaperPositions])

  const demoPaperLastBuyPriceByInstrumentToken = useMemo(() => {
    const r: Record<string, number> = {}
    for (const p of demoPaperPositions) {
      if (
        p.openContracts > 0 &&
        p.lastBuyPrice != null &&
        typeof p.lastBuyPrice === 'number' &&
        Number.isFinite(p.lastBuyPrice)
      )
        r[p.instrumentToken] = p.lastBuyPrice
    }
    return r
  }, [demoPaperPositions])

  const browseDemoPaperOpenBuys = useMemo(
    () => (chartRow ? demoPaperOpenBuysByInstrumentToken[chartRow.instrumentToken] ?? [] : []),
    [chartRow, demoPaperOpenBuysByInstrumentToken],
  )
  const browseDemoPaperLastBuyPrice = useMemo(
    () => (chartRow ? demoPaperLastBuyPriceByInstrumentToken[chartRow.instrumentToken] ?? null : null),
    [chartRow, demoPaperLastBuyPriceByInstrumentToken],
  )
  const manualTradePaperLastBuyPrice = useMemo(() => {
    const t = demoPaperToken.trim()
    if (!t) return null
    const v = demoPaperLastBuyPriceByInstrumentToken[t]
    return v != null && Number.isFinite(v) ? v : null
  }, [demoPaperToken, demoPaperLastBuyPriceByInstrumentToken])
  const [automationRecent, setAutomationRecent] = useState<MlAutomationRecentRow[]>([])
  const [automationRecentLoading, setAutomationRecentLoading] = useState(false)
  const [automationPriceModels, setAutomationPriceModels] = useState<PriceDirectionModelsApiResponse | null>(null)
  const [automationModelsLoading, setAutomationModelsLoading] = useState(false)
  const [automationReportEmailSending, setAutomationReportEmailSending] = useState(false)
  const [automationReportEmailSuccess, setAutomationReportEmailSuccess] = useState<string | null>(null)
  const [automationReportEmailError, setAutomationReportEmailError] = useState<string | null>(null)
  const [automationEmailReportRange, setAutomationEmailReportRange] = useState(initialAutomationEmailReportDatetimeLocal)
  const automationRecentRef = useRef(automationRecent)
  automationRecentRef.current = automationRecent
  const automationPriceModelsRef = useRef(automationPriceModels)
  automationPriceModelsRef.current = automationPriceModels
  const automationRecentSorted = useMemo(
    () => sortByPredictedAtNewestFirst(automationRecent),
    [automationRecent],
  )
  const [automationTableFilter, setAutomationTableFilter] = useState('')
  const [automationSortColumn, setAutomationSortColumn] =
    useState<AutomationRecentSortColumn>('predictedAt')
  const [automationSortHighFirst, setAutomationSortHighFirst] = useState(true)
  const [automationColFilterConfMin, setAutomationColFilterConfMin] = useState('')
  const [automationColFilterConfMax, setAutomationColFilterConfMax] = useState('')
  const [automationColFilterCategory, setAutomationColFilterCategory] = useState('')
  const [automationDirUp, setAutomationDirUp] = useState(true)
  const [automationDirDown, setAutomationDirDown] = useState(true)
  const [automationDirNeutral, setAutomationDirNeutral] = useState(true)
  const [automationOutcomeCorrect, setAutomationOutcomeCorrect] = useState(true)
  const [automationOutcomeWrong, setAutomationOutcomeWrong] = useState(true)
  const [automationOutcomePending, setAutomationOutcomePending] = useState(true)
  const [automationEngineOn, setAutomationEngineOn] = useState<Record<string, boolean>>({})
  const [automationIntervalOn, setAutomationIntervalOn] = useState<Record<string, boolean>>({})
  /** If every direction toggle is off, treat as “all directions” so the UI never goes blank by mistake. */
  const automationDirAccepted = useMemo(() => {
    if (!automationDirUp && !automationDirDown && !automationDirNeutral)
      return { up: true, down: true, neutral: true } as const
    return {
      up: automationDirUp,
      down: automationDirDown,
      neutral: automationDirNeutral,
    } as const
  }, [automationDirUp, automationDirDown, automationDirNeutral])
  /** Same guard when every outcome toggle is off. */
  const automationOutcomeAccepted = useMemo(() => {
    if (!automationOutcomeCorrect && !automationOutcomeWrong && !automationOutcomePending)
      return { correct: true, wrong: true, pending: true } as const
    return {
      correct: automationOutcomeCorrect,
      wrong: automationOutcomeWrong,
      pending: automationOutcomePending,
    } as const
  }, [automationOutcomeCorrect, automationOutcomeWrong, automationOutcomePending])

  const automationEngineIdsAvailable = useMemo(
    () => orderedAutomationEngineIds(automationPriceModels, automationRecentSorted),
    [automationPriceModels, automationRecentSorted],
  )
  /** Distinct engines in loaded rows — stable string across refetches when the set is unchanged. */
  const automationEnginesSignature = useMemo(
    () =>
      [...new Set(automationRecent.map((r) => r.engineModelId?.trim() ?? '').filter(Boolean))]
        .sort()
        .join('|'),
    [automationRecent],
  )
  const automationIntervalsAvailable = useMemo(() => {
    const ivs = new Set<string>()
    for (const r of automationRecentSorted) {
      const v = r.interval?.trim()
      if (v) ivs.add(v)
    }
    return sortMlAutomationIntervalCodes([...ivs])
  }, [automationRecentSorted])
  const automationIntervalsSignature = useMemo(
    () =>
      [...new Set(automationRecent.map((r) => r.interval?.trim() ?? '').filter(Boolean))]
        .sort()
        .join('|'),
    [automationRecent],
  )

  const automationRegistrySignature = useMemo(
    () => (automationPriceModels?.models ?? []).map((m) => m.id.trim()).filter(Boolean).join('|'),
    [automationPriceModels],
  )

  useEffect(() => {
    const sorted = sortByPredictedAtNewestFirst(automationRecentRef.current)
    const ids = orderedAutomationEngineIds(automationPriceModelsRef.current, sorted)
    setAutomationEngineOn((prev) => {
      const next: Record<string, boolean> = {}
      for (const id of ids) next[id] = prev[id] ?? true
      return next
    })
  }, [automationEnginesSignature, automationRegistrySignature])

  useEffect(() => {
    const sorted = sortByPredictedAtNewestFirst(automationRecentRef.current)
    const ivs = sortMlAutomationIntervalCodes([
      ...new Set(sorted.map((r) => r.interval?.trim() ?? '').filter(Boolean)),
    ])
    setAutomationIntervalOn((prev) => {
      const next: Record<string, boolean> = {}
      for (const iv of ivs) next[iv] = prev[iv] ?? true
      return next
    })
  }, [automationIntervalsSignature])

  const automationEnginePasses = useMemo(() => {
    if (automationEngineIdsAvailable.length === 0) return () => true
    const anyOn = automationEngineIdsAvailable.some((id) => automationEngineOn[id] ?? true)
    if (!anyOn) return () => true
    return (engineId: string) => automationEngineOn[engineId.trim()] ?? true
  }, [automationEngineIdsAvailable, automationEngineOn])

  const automationIntervalPasses = useMemo(() => {
    if (automationIntervalsAvailable.length === 0) return () => true
    const anyOn = automationIntervalsAvailable.some((iv) => automationIntervalOn[iv] ?? true)
    if (!anyOn) return () => true
    return (interval: string) => automationIntervalOn[interval.trim()] ?? true
  }, [automationIntervalsAvailable, automationIntervalOn])

  const automationRecentFiltered = useMemo(
    () =>
      automationRecentSorted.filter((r) =>
        MlAutomationRecentRowMatchesFilter(r, automationTableFilter, favoriteByInstrumentToken),
      ),
    [automationRecentSorted, automationTableFilter, favoriteByInstrumentToken],
  )
  const automationRecentToolbarFiltered = useMemo(
    () =>
      automationRecentFiltered.filter(
        (r) =>
          automationOutcomeAccepted[r.outcome] &&
          automationDirAccepted[r.direction] &&
          automationEnginePasses(r.engineModelId) &&
          automationIntervalPasses(r.interval),
      ),
    [
      automationRecentFiltered,
      automationOutcomeAccepted,
      automationDirAccepted,
      automationEnginePasses,
      automationIntervalPasses,
    ],
  )

  const automationCategoryFilterOptions = useMemo(() => {
    const labels = new Set<string>()
    for (const r of automationRecentToolbarFiltered) {
      labels.add(automationRowCategory(r, favoriteByInstrumentToken).label)
    }
    return [...labels].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }))
  }, [automationRecentToolbarFiltered, favoriteByInstrumentToken])

  useEffect(() => {
    const c = automationColFilterCategory.trim()
    if (!c) return
    if (!automationCategoryFilterOptions.includes(c)) setAutomationColFilterCategory('')
  }, [automationCategoryFilterOptions, automationColFilterCategory])

  const automationRecentTableFiltered = useMemo(() => {
    let rows = automationRecentToolbarFiltered
    const minRaw = automationColFilterConfMin.trim()
    const maxRaw = automationColFilterConfMax.trim()
    if (minRaw) {
      const n = Number.parseInt(minRaw, 10)
      if (Number.isFinite(n)) rows = rows.filter((r) => r.confidence >= n)
    }
    if (maxRaw) {
      const n = Number.parseInt(maxRaw, 10)
      if (Number.isFinite(n)) rows = rows.filter((r) => r.confidence <= n)
    }
    if (automationColFilterCategory.trim()) {
      const want = automationColFilterCategory.trim()
      rows = rows.filter(
        (r) => automationRowCategory(r, favoriteByInstrumentToken).label === want,
      )
    }
    return rows
  }, [
    automationRecentToolbarFiltered,
    automationColFilterConfMin,
    automationColFilterConfMax,
    automationColFilterCategory,
    favoriteByInstrumentToken,
  ])

  const automationRecentTableRows = useMemo(() => {
    const copy = [...automationRecentTableFiltered]
    copy.sort((a, b) =>
      compareAutomationRecentRows(
        a,
        b,
        automationSortColumn,
        automationSortHighFirst,
        favoriteByInstrumentToken,
      ),
    )
    return copy
  }, [
    automationRecentTableFiltered,
    automationSortColumn,
    automationSortHighFirst,
    favoriteByInstrumentToken,
  ])
  const [chartZoomByToken, setChartZoomByToken] = useState<Record<string, number>>({})
  const [chartIntervalByToken, setChartIntervalByToken] = useState<Record<string, ChartInterval>>({})
  const chartZoomSaveTimersRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})
  const chartIntervalSaveTimersRef = useRef<Record<string, ReturnType<typeof setTimeout>>>({})

  const persistInstrumentChartZoom = useCallback((instrumentToken: string, chartZoomStored: number | null) => {
    setChartZoomByToken((prev) => {
      const next = { ...prev }
      if (chartZoomStored == null) delete next[instrumentToken]
      else next[instrumentToken] = chartZoomStored
      return next
    })
    const timers = chartZoomSaveTimersRef.current
    const existing = timers[instrumentToken]
    if (existing) window.clearTimeout(existing)
    timers[instrumentToken] = window.setTimeout(() => {
      const body =
        chartZoomStored == null ? { instrumentToken } : { instrumentToken, visibleFraction: chartZoomStored }
      void api
        .put('/broker/kite/instruments/chart-zoom', body)
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

  /** All favorites + toolbar: one shared interval; clears per-symbol overrides on server for each favorite. */
  const applyIntervalToAllFavoriteCharts = useCallback(
    (iv: ChartInterval) => {
      setChartInterval(iv)
      setChartIntervalByToken((prev) => {
        const next = { ...prev }
        for (const f of favorites) {
          delete next[f.instrumentToken]
        }
        return next
      })
      for (const row of favorites) {
        void api
          .put('/broker/kite/instruments/chart-interval', {
            instrumentToken: row.instrumentToken,
            interval: null,
          })
          .catch(() => {
            /* non-fatal */
          })
      }
    },
    [favorites],
  )

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
      if (data.graphType === 'trend') {
        setMaLineVisibility((prev) => ({ ...prev, showLinearCloseTrend: true }))
      }
      setChartZoomByToken(
        data.zoomByInstrumentToken && typeof data.zoomByInstrumentToken === 'object'
          ? { ...data.zoomByInstrumentToken }
          : {},
      )
      setChartIntervalByToken(coerceChartIntervalOverrideMap(data.intervalByInstrumentToken))
      const serverTrend = data.trendAnalysisIntervals
      if (Array.isArray(serverTrend) && serverTrend.length > 0) {
        const next: ChartInterval[] = []
        for (const entry of serverTrend) {
          const s = String(entry ?? '')
          if ((CHART_INTERVALS as readonly string[]).includes(s)) next.push(s as ChartInterval)
        }
        if (next.length > 0) setTrendAnalysisSelections(orderTrendSelections(next))
        else setTrendAnalysisSelections(loadTrendAnalysisSelections())
      } else {
        setTrendAnalysisSelections(loadTrendAnalysisSelections())
      }
      setFavoriteMlAutomationEnabled(Boolean(data.mlAutomationEnabled))
      const rawAutoIv = typeof data.mlAutomationInterval === 'string' ? data.mlAutomationInterval.trim() : ''
      setFavoriteMlAutomationBarInterval(
        rawAutoIv && (CHART_INTERVALS as readonly string[]).includes(rawAutoIv) ? rawAutoIv : '',
      )
      setFavoriteMlAutomationPollInput(
        typeof data.mlAutomationPollIntervalMinutes === 'number' &&
          data.mlAutomationPollIntervalMinutes >= 1 &&
          data.mlAutomationPollIntervalMinutes <= 1440
          ? String(data.mlAutomationPollIntervalMinutes)
          : '',
      )
      setFavoriteMlAutomationMinSecAfterOpenInput(
        typeof data.mlAutomationMinSecondsAfterBarOpen === 'number' &&
          data.mlAutomationMinSecondsAfterBarOpen >= 0 &&
          data.mlAutomationMinSecondsAfterBarOpen <= 86400
          ? String(data.mlAutomationMinSecondsAfterBarOpen)
          : '',
      )
      mlAutomationPollTouchedRef.current = false
      mlAutomationMinSecAfterOpenTouchedRef.current = false
      setMlAutomationError(null)
      setDemoAutoTradeEnabled(Boolean(data.demoAutoTradeEnabled))
      if (typeof data.demoAutoTradeNotionalInr === 'number' && Number.isFinite(data.demoAutoTradeNotionalInr)) {
        setDemoAutoTradeNotionalInr(data.demoAutoTradeNotionalInr)
      } else {
        setDemoAutoTradeNotionalInr(10_000)
      }
      const rawStrat =
        typeof data.demoAutoTradeStrategy === 'string' ? data.demoAutoTradeStrategy.trim() : ''
      if (rawStrat && DEMO_AUTO_TRADE_STRATEGY_OPTIONS.some((o) => o.id === rawStrat))
        setDemoAutoTradeStrategy(rawStrat)
      else setDemoAutoTradeStrategy('equal_split')
    } catch {
      // keep defaults
    } finally {
      setChartPrefsHydrated(true)
    }
  }, [])

  useEffect(() => {
    void loadChartSettings()
  }, [loadChartSettings])

  const loadDemoEodSummary = useCallback(async () => {
    try {
      setDemoEodLoading(true)
      setDemoEodError(null)
      const { data } = await api.get<DemoAutoTradeEodSummaryDto>(
        '/broker/kite/instruments/demo-auto-trade/eod-summary',
      )
      setDemoEodSummary(data)
      setDemoAutoTradeEnabled(Boolean(data.demoAutoTradeEnabled))
      if (typeof data.demoNotionalInr === 'number' && Number.isFinite(data.demoNotionalInr)) {
        setDemoAutoTradeNotionalInr(data.demoNotionalInr)
      }
      const sid = typeof data.demoAutoTradeStrategy === 'string' ? data.demoAutoTradeStrategy.trim() : ''
      if (sid && DEMO_AUTO_TRADE_STRATEGY_OPTIONS.some((o) => o.id === sid)) setDemoAutoTradeStrategy(sid)
    } catch (err) {
      setDemoEodError(problemDetail(err))
      setDemoEodSummary(null)
    } finally {
      setDemoEodLoading(false)
    }
  }, [])

  const loadDemoTodayLegs = useCallback(async () => {
    try {
      setDemoTodayLegsLoading(true)
      setDemoTodayLegsError(null)
      const { data } = await api.get<DemoAutoTradeTodayLegsDto>(
        '/broker/kite/instruments/demo-auto-trade/today-legs',
      )
      setDemoTodayLegs(data)
      if (typeof data.demoNotionalInr === 'number' && Number.isFinite(data.demoNotionalInr)) {
        setDemoAutoTradeNotionalInr(data.demoNotionalInr)
      }
    } catch (err) {
      setDemoTodayLegsError(problemDetail(err))
      setDemoTodayLegs(null)
    } finally {
      setDemoTodayLegsLoading(false)
    }
  }, [])

  const loadDemoPaperPositions = useCallback(async () => {
    try {
      setDemoPaperPositionsLoading(true)
      setDemoPaperPositionsError(null)
      const { data } = await api.get<DemoPaperPositionListItemDto[]>(
        '/broker/kite/instruments/demo-paper-positions',
      )
      setDemoPaperPositions(
        Array.isArray(data)
          ? data.map((row) => ({
              ...row,
              openBuys: Array.isArray(row.openBuys) ? row.openBuys : [],
              lastBuyPrice: (() => {
                const v = row.lastBuyPrice
                if (typeof v === 'number' && Number.isFinite(v)) return v
                if (typeof v === 'string') {
                  const n = Number.parseFloat(v)
                  return Number.isFinite(n) ? n : null
                }
                return null
              })(),
            }))
          : [],
      )
    } catch (err) {
      setDemoPaperPositionsError(problemDetail(err))
      setDemoPaperPositions([])
    } finally {
      setDemoPaperPositionsLoading(false)
    }
  }, [])

  const loadDemoPaperTrades = useCallback(async () => {
    try {
      setDemoPaperTradesLoading(true)
      setDemoPaperTradesError(null)
      const { data } = await api.get<DemoPaperTradeHistoryRowDto[]>('/broker/kite/instruments/demo-paper-trades', {
        params: { take: 500 },
      })
      setDemoPaperTrades(Array.isArray(data) ? data : [])
    } catch (err) {
      setDemoPaperTradesError(problemDetail(err))
      setDemoPaperTrades([])
    } finally {
      setDemoPaperTradesLoading(false)
    }
  }, [])

  const executeDemoPaperTrade = useCallback(
    async (side: 'buy' | 'sell', instrumentToken: string) => {
      setDemoPaperTradeError(null)
      setDemoPaperTradeLast(null)
      const n = Number.parseInt(demoPaperContracts.trim(), 10)
      const token = instrumentToken.trim()
      if (!token) {
        setDemoPaperTradeError('Pick a locked instrument.')
        return
      }
      if (!Number.isFinite(n) || n < 1) {
        setDemoPaperTradeError('Lots must be a whole number ≥ 1.')
        return
      }
      setDemoPaperTradeBusy({ side, instrumentToken: token })
      try {
        const { data } = await api.post<DemoPaperTradeResultDto>(
          '/broker/kite/instruments/demo-paper-trade',
          {
            instrumentToken: token,
            side,
            contracts: n,
          },
        )
        setDemoPaperToken(token)
        setDemoAutoTradeNotionalInr(data.walletBalanceAfter)
        setDemoPaperTradeLast(
          `${side.toUpperCase()} ${data.contracts} lot${data.contracts === 1 ? '' : 's'} × ${data.tradingsymbol} @ ${data.lastPrice.toFixed(4)} (lot size ${data.lotSize}) — cash ${formatInrRupee(data.cashFlowInr)} · wallet ${formatInrRupee(data.walletBalanceAfter)} · open ${data.openContractsAfter}`,
        )
        await Promise.all([loadDemoPaperPositions(), loadDemoPaperTrades()])
        await loadDemoEodSummary()
      } catch (err) {
        setDemoPaperTradeError(problemDetail(err))
      } finally {
        setDemoPaperTradeBusy(null)
      }
    },
    [demoPaperContracts, loadDemoPaperPositions, loadDemoPaperTrades, loadDemoEodSummary],
  )

  const loadDemoFullReport = useCallback(
    async (mode: 'seven' | 'merged') => {
      setDemoFullReportLoading(true)
      setDemoFullReportError(null)
      try {
        let path = '/broker/kite/instruments/demo-auto-trade/full-report'
        if (mode === 'merged') {
          const fromTrim = automationEmailReportRange.from.trim()
          const toTrim = automationEmailReportRange.to.trim()
          if (!fromTrim || !toTrim) {
            setDemoFullReportError(
              'Set both From and To under Merged log range & email, or use Last 7 report days.',
            )
            setDemoFullReport(null)
            return
          }
          const fromMs = Date.parse(fromTrim)
          const toMs = Date.parse(toTrim)
          if (!Number.isFinite(fromMs) || !Number.isFinite(toMs)) {
            setDemoFullReportError('Invalid date/time in From / To.')
            setDemoFullReport(null)
            return
          }
          if (fromMs >= toMs) {
            setDemoFullReportError('From must be strictly before To (half-open range on PredictedAt).')
            setDemoFullReport(null)
            return
          }
          const maxSpanMs = 93 * 24 * 60 * 60 * 1000
          if (toMs - fromMs > maxSpanMs) {
            setDemoFullReportError('Range cannot exceed 93 days.')
            setDemoFullReport(null)
            return
          }
          const q = new URLSearchParams({
            fromUtc: new Date(fromMs).toISOString(),
            toUtcExclusive: new Date(toMs).toISOString(),
          })
          path = `${path}?${q.toString()}`
        }
        const { data } = await api.get<DemoAutoTradeFullReportDto>(path)
        setDemoFullReport(data)
      } catch (err) {
        setDemoFullReportError(problemDetail(err))
        setDemoFullReport(null)
      } finally {
        setDemoFullReportLoading(false)
      }
    },
    [automationEmailReportRange.from, automationEmailReportRange.to],
  )

  const demoFullReportPnlChartRows = useMemo(() => {
    const days = demoFullReport?.dailySummaries ?? []
    if (days.length === 0) return []
    const sorted = [...days].sort((a, b) => a.reportDate.localeCompare(b.reportDate))
    let cumulativeNet = 0
    return sorted.map((d) => {
      cumulativeNet += d.hypotheticalTotalPnlInr
      return {
        reportDate: d.reportDate,
        /** Short axis label (calendar sort stays on full date). */
        dayLabel: d.reportDate.length >= 10 ? d.reportDate.slice(5, 10) : d.reportDate,
        netPnl: d.hypotheticalTotalPnlInr,
        grossPnl: d.hypotheticalGrossPnlInr,
        charges: d.hypotheticalChargesInr,
        cumulativeNet,
      }
    })
  }, [demoFullReport?.dailySummaries])

  const demoTodayLegsPnlChartRows = useMemo(() => {
    const legs = demoTodayLegs?.legs ?? []
    const allocated = legs.filter((l) => l.status === 'allocated')
    if (allocated.length === 0) return []
    const sorted = [...allocated].sort((a, b) => a.predictedAtUtc.localeCompare(b.predictedAtUtc))
    return sorted.map((leg) => {
      const raw =
        leg.tradingsymbol?.trim() ||
        (leg.exchange?.trim() ? `${leg.exchange.trim()}:${leg.instrumentToken}` : leg.instrumentToken)
      const sym = raw.length > 14 ? `${raw.slice(0, 13)}…` : raw
      return {
        key: leg.predictionId,
        symbolLabel: sym,
        symbolFull: raw,
        netPnl: leg.legNetPnlInr,
      }
    })
  }, [demoTodayLegs?.legs])

  const persistDemoAutoTrade = useCallback(
    async (nextEnabled: boolean, nextStrategy: string) => {
      setDemoAutoTradeSaving(true)
      setDemoEodError(null)
      try {
        await api.put('/broker/kite/instruments/demo-auto-trade', {
          enabled: nextEnabled,
          strategy: nextStrategy,
        })
        setDemoAutoTradeEnabled(nextEnabled)
        setDemoAutoTradeStrategy(nextStrategy)
        await loadDemoEodSummary()
        await loadDemoTodayLegs()
        await loadChartSettings()
      } catch (err) {
        setDemoEodError(problemDetail(err))
      } finally {
        setDemoAutoTradeSaving(false)
      }
    },
    [loadChartSettings, loadDemoEodSummary, loadDemoTodayLegs],
  )

  useEffect(() => {
    if (mainTab !== 'autoTrading' || !chartPrefsHydrated) return
    void loadDemoEodSummary()
  }, [mainTab, chartPrefsHydrated, loadDemoEodSummary])

  useEffect(() => {
    if (!chartPrefsHydrated) return
    const t = window.setTimeout(() => {
      void api
        .put('/broker/kite/instruments/chart-settings', {
          interval: chartInterval,
          rangePreset: chartRangePreset,
          graphType: chartGraphType,
          mlAutomationEnabled: favoriteMlAutomationEnabled,
          trendAnalysisIntervals: orderTrendSelections(trendAnalysisSelections),
        })
        .catch(() => {
          /* non-fatal */
        })
    }, 400)
    return () => window.clearTimeout(t)
  }, [
    chartInterval,
    chartRangePreset,
    chartGraphType,
    chartPrefsHydrated,
    favoriteMlAutomationEnabled,
    trendAnalysisSelections,
  ])

  const loadAutomationRecent = useCallback(async () => {
    try {
      setAutomationRecentLoading(true)
      const fromTrim = automationEmailReportRange.from.trim()
      const toTrim = automationEmailReportRange.to.trim()
      const fromMs = Date.parse(fromTrim)
      const toMs = Date.parse(toTrim)
      const maxSpanMs = 93 * 24 * 60 * 60 * 1000
      const rangeOk =
        fromTrim.length > 0 &&
        toTrim.length > 0 &&
        Number.isFinite(fromMs) &&
        Number.isFinite(toMs) &&
        fromMs < toMs &&
        toMs - fromMs <= maxSpanMs
      const { data } = await api.get<MlAutomationRecentRow[]>('/predictions/price-direction/automation-recent', {
        params: {
          take: ML_AUTOMATION_RECENT_FETCH_TAKE,
          ...(rangeOk
            ? { fromUtc: new Date(fromMs).toISOString(), toUtcExclusive: new Date(toMs).toISOString() }
            : {}),
        },
      })
      setAutomationRecent(data)
    } catch {
      /* non-fatal */
    } finally {
      setAutomationRecentLoading(false)
    }
  }, [automationEmailReportRange.from, automationEmailReportRange.to])

  const loadStatus = useCallback(async () => {
    setStatusLoading(true)
    try {
      const { data } = await api.get<BrokerStatusResponse>('/broker/status')
      setProvider(data.connected ? (data.provider ?? null) : null)
    } catch {
      setProvider(null)
    } finally {
      setStatusLoading(false)
    }
  }, [])

  const isZerodha = provider?.toLowerCase() === 'zerodha'

  useEffect(() => {
    if (mainTab !== 'manualTrade' || !chartPrefsHydrated) return
    void loadChartSettings()
  }, [mainTab, chartPrefsHydrated, loadChartSettings])

  useEffect(() => {
    if (
      (mainTab !== 'autoTrading' && mainTab !== 'manualTrade') ||
      !chartPrefsHydrated ||
      !isZerodha
    )
      return
    void Promise.all([loadDemoPaperPositions(), loadDemoPaperTrades()])
  }, [mainTab, chartPrefsHydrated, isZerodha, loadDemoPaperPositions, loadDemoPaperTrades])

  useEffect(() => {
    if (mainTab !== 'autoTrading' && mainTab !== 'manualTrade') return
    if (tradingLocks.length === 0) return
    setDemoPaperToken((t) => {
      if (t && tradingLocks.some((l) => l.instrumentToken === t)) return t
      return tradingLocks[0]!.instrumentToken
    })
  }, [mainTab, tradingLocks])

  useEffect(() => {
    if (mainTab !== 'autoTrading' || !chartPrefsHydrated || !isZerodha) return
    void loadDemoTodayLegs()
    const id = window.setInterval(() => {
      void loadDemoTodayLegs()
    }, 12_000)
    return () => window.clearInterval(id)
  }, [mainTab, chartPrefsHydrated, isZerodha, loadDemoTodayLegs])

  const loadAutomationPriceModels = useCallback(async () => {
    if (!isZerodha) {
      setAutomationPriceModels(null)
      setAutomationModelsLoading(false)
      return
    }
    setAutomationModelsLoading(true)
    try {
      const { data } = await api.get<PriceDirectionModelsApiResponse>('/predictions/price-direction/models')
      setAutomationPriceModels(data)
    } catch {
      setAutomationPriceModels(null)
    } finally {
      setAutomationModelsLoading(false)
    }
  }, [isZerodha])

  const saveFavoriteMlAutomationSchedule = useCallback(async () => {
    setMlAutomationSaving(true)
    setMlAutomationError(null)
    try {
      const intervalNorm =
        favoriteMlAutomationBarInterval.trim() === ''
          ? ''
          : favoriteMlAutomationBarInterval.trim()
      if (
        intervalNorm !== '' &&
        !(CHART_INTERVALS as readonly string[]).includes(intervalNorm)
      ) {
        setMlAutomationError('Pick a candle interval from the list, or inherit (empty).')
        return
      }
      const body: {
        enabled: boolean
        interval: string
        pollIntervalMinutes?: number
        minSecondsAfterBarOpenForAutomation?: number | null
      } = {
        enabled: favoriteMlAutomationEnabled,
        interval: intervalNorm,
      }
      if (mlAutomationPollTouchedRef.current) {
        const raw = favoriteMlAutomationPollInput.trim()
        if (raw === '') body.pollIntervalMinutes = 0
        else {
          const n = parseInt(raw, 10)
          if (!Number.isFinite(n) || n < 1 || n > 1440) {
            setMlAutomationError(
              'Every N min: leave blank (inherit) or enter a whole number 1–1440.',
            )
            return
          }
          body.pollIntervalMinutes = n
        }
      }
      if (mlAutomationMinSecAfterOpenTouchedRef.current) {
        const rawMin = favoriteMlAutomationMinSecAfterOpenInput.trim()
        if (rawMin === '') body.minSecondsAfterBarOpenForAutomation = null
        else {
          const sec = parseInt(rawMin, 10)
          if (!Number.isFinite(sec) || sec < 0 || sec > 86400) {
            setMlAutomationError(
              'Min seconds after bar open: leave blank (use server default) or enter a whole number 0–86400.',
            )
            return
          }
          body.minSecondsAfterBarOpenForAutomation = sec
        }
      }
      await api.put('/broker/kite/instruments/favorite-ml-automation', body)
      mlAutomationPollTouchedRef.current = false
      mlAutomationMinSecAfterOpenTouchedRef.current = false
      void loadChartSettings()
      void loadAutomationRecent()
    } catch (err) {
      setMlAutomationError(problemDetail(err))
    } finally {
      setMlAutomationSaving(false)
    }
  }, [
    favoriteMlAutomationEnabled,
    favoriteMlAutomationBarInterval,
    favoriteMlAutomationPollInput,
    favoriteMlAutomationMinSecAfterOpenInput,
    loadChartSettings,
    loadAutomationRecent,
  ])

  // Poll merged automation rows while Auto predictions, All favorites, or Locked is visible
  // (favorite tiles show a per-symbol strip; Browse stays quiet to avoid extra API load).
  useEffect(() => {
    if (!isZerodha) {
      setAutomationRecent([])
      setAutomationPriceModels(null)
      return
    }
    if (!(mainTab === 'automation' || mainTab === 'favorites' || mainTab === 'tradingLocks')) return
    const id = window.setInterval(() => {
      if (document.visibilityState === 'visible') void loadAutomationRecent()
    }, 60_000)
    return () => window.clearInterval(id)
  }, [isZerodha, mainTab, loadAutomationRecent])

  useEffect(() => {
    if (!isZerodha) return
    if (!(mainTab === 'automation' || mainTab === 'favorites' || mainTab === 'tradingLocks')) return
    void loadAutomationRecent()
  }, [isZerodha, mainTab, automationEmailReportRange.from, automationEmailReportRange.to, loadAutomationRecent])

  useEffect(() => {
    if (!isZerodha) return
    if (!(mainTab === 'automation' || mainTab === 'favorites' || mainTab === 'tradingLocks')) return
    void loadAutomationPriceModels()
  }, [isZerodha, mainTab, loadAutomationPriceModels])

  const liveMarket = useLiveMarketTick(chartRow?.instrumentToken ?? null, isZerodha && mainTab === 'browse' && !!chartRow)
  const liveLtp = liveMarket.lastPrice
  const liveLastTick = liveMarket.lastTick

  const loadInstruments = useCallback(async () => {
    if (!isZerodha) {
      setInstruments(null)
      setInstrumentsError(null)
      setInstrumentsLoading(false)
      setTodayTopPerformers([])
      setTodayTopVisibleCount(TODAY_TOP_MOVERS_PAGE_SIZE)
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
      setTodayTopVisibleCount(TODAY_TOP_MOVERS_PAGE_SIZE)
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
        { params: { take: TODAY_TOP_MOVERS_FETCH_TAKE } },
      )
      setTodayTopPerformers(data.items ?? [])
      setTodayTopVisibleCount(TODAY_TOP_MOVERS_PAGE_SIZE)
      setTodayTopBasis(data.basis ?? '')
    } catch (err) {
      setTodayTopPerformers([])
      setTodayTopVisibleCount(TODAY_TOP_MOVERS_PAGE_SIZE)
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
    const id = window.setInterval(() => {
      if (document.visibilityState === 'visible') void loadStatus()
    }, 90_000)
    return () => window.clearInterval(id)
  }, [loadStatus])

  useEffect(() => {
    void loadInstruments()
  }, [loadInstruments])

  useEffect(() => {
    if (!isZerodha) {
      setFavorites([])
      setFavoritesError(null)
      setTradingLocks([])
      setTradingLocksError(null)
      return
    }
    void loadFavorites()
    void loadTradingLocks()
  }, [isZerodha, loadFavorites, loadTradingLocks])

  useEffect(() => {
    if (!(isZerodha && (mainTab === 'automation' || mainTab === 'autoTrading' || mainTab === 'manualTrade'))) return
    void loadTradingLocks()
  }, [mainTab, isZerodha, loadTradingLocks])

  return (
    <Layout>
      <h1 className="h3 mb-1">F&O, spot & commodities</h1>
      <p className="text-secondary small mb-4" style={{ maxWidth: '42rem' }}>
        Browse F&amp;O and MCX from Kite&apos;s daily dumps; search <strong>Spot</strong> for NSE/BSE equity cash (EQ) and chart
        with the same OHLC tools. Lists are not live quotes until you select a row.
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
                  <strong>All favorites</strong> and <strong>Locked for trading</strong>, F&amp;O + Spot + MCX). Favorites, locks, and chart settings sync to your account; use ☆/★ and 🔓/🔒 on browse rows;
                  open <strong>All favorites</strong> or <strong>Locked for trading</strong> for multi-chart grids. On <strong>Browse</strong>, click a row for the chart below.                   Scheduled
                  automation and the merged prediction log live on <strong>Auto predictions</strong>; hypothetical{' '}
                  <strong>Demo auto-trade</strong> settings and reports live on that tab&apos;s sibling.
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
              <Nav.Item>
                <Nav.Link eventKey="tradingLocks">Locked for trading ({tradingLocks.length})</Nav.Link>
              </Nav.Item>
              <Nav.Item>
                <Nav.Link eventKey="automation">Auto predictions</Nav.Link>
              </Nav.Item>
              <Nav.Item>
                <Nav.Link eventKey="manualTrade">Manual trade</Nav.Link>
              </Nav.Item>
              <Nav.Item>
                <Nav.Link eventKey="autoTrading">Demo auto-trade</Nav.Link>
              </Nav.Item>
            </Nav>

            {mainTab === 'automation' ? (
            <div className="mt-3 d-flex flex-column gap-3">
              {!isZerodha ? (
                <Alert variant="secondary" className="py-2 small mb-0 border border-secondary-subtle shadow-sm">
                  <span className="fw-semibold text-body">Kite session required.</span> Connect{' '}
                  <strong>Zerodha</strong> to change automation, refresh the merged log, and send report emails.
                </Alert>
              ) : null}

              <div className="border-bottom border-secondary-subtle pb-3 mb-1">
                <h2 className="h6 text-body mb-1">Auto ML predictions</h2>
                <p className="small text-secondary mb-0">
                  Server schedule, merged log, charts, and the recent automation table cover favorites in the loaded range.
                </p>
              </div>

              <div className="rounded-3 border border-secondary-subtle shadow-sm overflow-hidden">
                <div className="px-3 py-2 border-bottom border-secondary-subtle bg-body-secondary d-flex flex-wrap align-items-center justify-content-between gap-2">
                  <span className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-0">
                    Server schedule
                  </span>
                  <div className="d-flex flex-wrap align-items-center gap-2 small">
                    <Badge bg="dark" className="font-monospace px-2">
                      m
                    </Badge>
                    <span className="text-muted">model candles</span>
                    <span className="text-secondary">·</span>
                    <Badge bg="secondary" className="font-monospace px-2">
                      N
                    </Badge>
                    <span className="text-muted">pass cadence</span>
                  </div>
                </div>
                <div className="p-3 p-md-4 bg-body-tertiary bg-opacity-25">
                  {mlAutomationError ? (
                    <Alert variant="warning" className="py-2 small mb-3">
                      {mlAutomationError}
                    </Alert>
                  ) : null}

                  <Row className="g-4 align-items-stretch">
                    <Col xs={12} xl={4}>
                      <div className="h-100 p-3 rounded-3 border border-secondary-subtle bg-body d-flex flex-column">
                        <Form.Check
                          type="switch"
                          id="favorite-ml-automation-switch"
                          className="mb-2"
                          label={
                            <span className="fw-semibold">
                              Auto ML for favorites
                              <span className="d-block small fw-normal text-secondary mt-1 lh-sm">
                                Runs on the server for starred instruments when host automation is enabled.
                              </span>
                            </span>
                          }
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
                                void loadChartSettings()
                                void loadAutomationRecent()
                              })
                              .catch((err) => setMlAutomationError(problemDetail(err)))
                              .finally(() => setMlAutomationSaving(false))
                          }}
                        />
                        <div className="mt-auto pt-2">
                          {favoriteMlAutomationEnabled ? (
                            <Badge bg="success" pill>
                              On
                            </Badge>
                          ) : (
                            <Badge bg="secondary" pill>
                              Off
                            </Badge>
                          )}
                        </div>
                      </div>
                    </Col>
                    <Col xs={12} xl={8}>
                      <div className="p-3 rounded-3 border border-secondary-subtle bg-body h-100">
                        <Row className="g-3 align-items-start">
                          <Col xs={12} sm={6} lg={4}>
                            <Form.Group className="mb-0">
                              <Form.Label className="small text-secondary mb-1 d-flex align-items-center gap-2 flex-wrap">
                                Model interval
                                <Badge bg="dark" className="font-monospace" style={{ fontSize: '0.65rem' }}>
                                  m
                                </Badge>
                              </Form.Label>
                              <Form.Select
                                size="sm"
                                className="font-monospace"
                                value={favoriteMlAutomationBarInterval}
                                onChange={(e) => setFavoriteMlAutomationBarInterval(e.target.value)}
                                disabled={!chartPrefsHydrated || mlAutomationSaving || !isZerodha}
                                aria-label="Candle interval for server auto ML on favorites"
                              >
                                <option value="">Inherit (server / chart)</option>
                                {CHART_INTERVALS.map((iv) => (
                                  <option key={iv} value={iv}>
                                    {iv}
                                  </option>
                                ))}
                              </Form.Select>
                            </Form.Group>
                          </Col>
                          <Col xs={12} sm={6} lg={4}>
                            <Form.Group className="mb-0">
                              <Form.Label className="small text-secondary mb-1 d-flex align-items-center gap-2 flex-wrap">
                                Pass every (min)
                                <Badge bg="secondary" className="font-monospace" style={{ fontSize: '0.65rem' }}>
                                  N
                                </Badge>
                              </Form.Label>
                              <Form.Control
                                type="number"
                                inputMode="numeric"
                                min={1}
                                max={1440}
                                step={1}
                                size="sm"
                                placeholder="—"
                                value={favoriteMlAutomationPollInput}
                                onChange={(e) => {
                                  mlAutomationPollTouchedRef.current = true
                                  setFavoriteMlAutomationPollInput(e.target.value)
                                }}
                                disabled={!chartPrefsHydrated || mlAutomationSaving || !isZerodha}
                                aria-label="Minimum whole minutes between automated new prediction pass starts (N); when set, server does not wait for the m-bar to close"
                              />
                              <Form.Text className="text-muted" style={{ fontSize: '0.68rem' }}>
                                Blank = inherit. When set, spacing only—no wait for{' '}
                                <span className="font-monospace">m</span> bar close.
                              </Form.Text>
                            </Form.Group>
                          </Col>
                          <Col xs={12} sm={6} lg={4}>
                            <Form.Group className="mb-0">
                              <Form.Label className="small text-secondary mb-1">Intrabar delay (sec)</Form.Label>
                              <Form.Control
                                type="number"
                                inputMode="numeric"
                                min={0}
                                max={86400}
                                step={1}
                                size="sm"
                                placeholder="—"
                                value={favoriteMlAutomationMinSecAfterOpenInput}
                                onChange={(e) => {
                                  mlAutomationMinSecAfterOpenTouchedRef.current = true
                                  setFavoriteMlAutomationMinSecAfterOpenInput(e.target.value)
                                }}
                                disabled={!chartPrefsHydrated || mlAutomationSaving || !isZerodha}
                                aria-label="Minimum seconds after the current candle open before new auto ML rows (intrabar; blank = server default)"
                              />
                              <Form.Text className="text-muted" style={{ fontSize: '0.68rem' }}>
                                Only when <span className="font-monospace">N</span> is blank. Blank = server default.
                              </Form.Text>
                            </Form.Group>
                          </Col>
                        </Row>
                        <div className="mt-3 pt-2 border-top border-secondary-subtle d-flex flex-wrap justify-content-end gap-2">
                          <Button
                            type="button"
                            variant="primary"
                            size="sm"
                            disabled={!chartPrefsHydrated || mlAutomationSaving || !isZerodha}
                            title="Saves m / N / intrabar settings to your account (independent of the chart toolbar)."
                            onClick={() => void saveFavoriteMlAutomationSchedule()}
                          >
                            {mlAutomationSaving ? 'Saving…' : 'Save schedule'}
                          </Button>
                        </div>
                      </div>
                    </Col>
                  </Row>
                </div>
              </div>

              <div className="rounded-3 border border-secondary-subtle shadow-sm overflow-hidden">
                <div className="px-3 py-2 border-bottom border-secondary-subtle bg-body-secondary">
                  <span className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-0">
                    Merged log range &amp; email
                  </span>
                </div>
                <div className="p-3 p-md-4 bg-body-tertiary bg-opacity-25">
                  <Row className="g-3 align-items-end">
                    <Col xs={12} md={6} lg={5} xl={4}>
                      <Form.Group className="mb-0">
                        <Form.Label column={false} className="small text-secondary mb-1">
                          From (local)
                        </Form.Label>
                        <Form.Control
                          type="datetime-local"
                          step={60}
                          size="sm"
                          className="font-monospace w-100"
                          style={{ minWidth: 0 }}
                          value={automationEmailReportRange.from}
                          onChange={(e) =>
                            setAutomationEmailReportRange((p) => ({ ...p, from: e.target.value }))
                          }
                          disabled={automationReportEmailSending || !isZerodha}
                        />
                      </Form.Group>
                    </Col>
                    <Col xs={12} md={6} lg={5} xl={4}>
                      <Form.Group className="mb-0">
                        <Form.Label column={false} className="small text-secondary mb-1">
                          To (exclusive)
                        </Form.Label>
                        <Form.Control
                          type="datetime-local"
                          step={60}
                          size="sm"
                          className="font-monospace w-100"
                          style={{ minWidth: 0 }}
                          value={automationEmailReportRange.to}
                          onChange={(e) =>
                            setAutomationEmailReportRange((p) => ({ ...p, to: e.target.value }))
                          }
                          disabled={automationReportEmailSending || !isZerodha}
                        />
                      </Form.Group>
                    </Col>
                  </Row>
                  <Row className="g-2 mt-3 align-items-center">
                    <Col xs={12} className="d-flex flex-wrap gap-2 align-items-center">
                      <Button
                        type="button"
                        variant="outline-secondary"
                        size="sm"
                        disabled={automationReportEmailSending || !isZerodha}
                        title="Restores defaults: local midnight today through the current minute."
                        onClick={() => setAutomationEmailReportRange(initialAutomationEmailReportDatetimeLocal())}
                      >
                        Today → now
                      </Button>
                      <Button
                        type="button"
                        variant="outline-secondary"
                        size="sm"
                        disabled={automationRecentLoading || !isZerodha}
                        onClick={() => {
                          void loadAutomationRecent()
                          void loadAutomationPriceModels()
                        }}
                      >
                        {automationRecentLoading ? 'Loading…' : 'Refresh list'}
                      </Button>
                      <Button
                        type="button"
                        variant="outline-primary"
                        size="sm"
                        className="ms-sm-auto"
                        disabled={automationReportEmailSending || !isZerodha}
                        title="Emails automation rows whose PredictedAt falls in [fromUtc, toUtcExclusive) for the date range to the left (sent as UTC). HTML body includes inline PNG outcome charts (cid-linked, combined + each engine); CSV attached. Requires SMTP and a saved profile email."
                        onClick={() => {
                          setAutomationReportEmailError(null)
                          setAutomationReportEmailSuccess(null)
                          const fromTrim = automationEmailReportRange.from.trim()
                          const toTrim = automationEmailReportRange.to.trim()
                          if (!fromTrim || !toTrim) {
                            setAutomationReportEmailError('Pick both report start and end (browser-local date/time).')
                            return
                          }
                          const fromMs = Date.parse(fromTrim)
                          const toMs = Date.parse(toTrim)
                          if (!Number.isFinite(fromMs) || !Number.isFinite(toMs)) {
                            setAutomationReportEmailError('Invalid date/time.')
                            return
                          }
                          if (fromMs >= toMs) {
                            setAutomationReportEmailError(
                              'Start must be strictly before end. The API uses PredictedAt in [start, end exclusive).',
                            )
                            return
                          }
                          const maxSpanMs = 93 * 24 * 60 * 60 * 1000
                          if (toMs - fromMs > maxSpanMs) {
                            setAutomationReportEmailError('Range cannot exceed 93 days.')
                            return
                          }
                          setAutomationReportEmailSending(true)
                          void api
                            .post<ManualAutomationEmailReportResponse>(
                              '/predictions/price-direction/automation-report-email',
                              {
                                fromUtc: new Date(fromMs).toISOString(),
                                toUtcExclusive: new Date(toMs).toISOString(),
                              },
                            )
                            .then(({ data }) => {
                              setAutomationReportEmailSuccess(
                                `Email sent: ${data.rowCount} automation row${data.rowCount === 1 ? '' : 's'} (${data.reportRangeSummary}; ${data.pieChartsAttached} HTML chart section${data.pieChartsAttached === 1 ? '' : 's'} + CSV attachment).`,
                              )
                            })
                            .catch((err) => setAutomationReportEmailError(problemDetail(err)))
                            .finally(() => setAutomationReportEmailSending(false))
                        }}
                      >
                        {automationReportEmailSending ? 'Sending…' : 'Email report'}
                      </Button>
                    </Col>
                  </Row>
                  {automationReportEmailSuccess ? (
                    <Alert
                      variant="success"
                      className="py-2 small mt-3 mb-0"
                      dismissible
                      onClose={() => setAutomationReportEmailSuccess(null)}
                    >
                      {automationReportEmailSuccess}
                    </Alert>
                  ) : null}
                  {automationReportEmailError ? (
                    <Alert
                      variant="warning"
                      className="py-2 small mt-3 mb-0"
                      dismissible
                      onClose={() => setAutomationReportEmailError(null)}
                    >
                      {automationReportEmailError}
                    </Alert>
                  ) : null}
                </div>
              </div>

              <details className="small border border-secondary-subtle rounded-3 px-3 py-2 bg-body user-select-none">
                <summary
                  className="fw-semibold text-body py-1"
                  style={{ cursor: 'pointer' }}
                  aria-label="Expand help for automation m, N, and server quiet hours"
                >
                  How <span className="font-monospace">m</span>, <span className="font-monospace">N</span>, filters, and
                  quiet hours work
                </summary>
                <div className="text-secondary pt-2 pb-1">
                  <p className="mb-2">
                    When enabled, the API runs next-bar predictions for each favorite using <strong>every</strong> registered
                    ML engine (subset via server{' '}
                    <span className="font-monospace">FavoriteMlAutomation:PredictionModelId</span>); LightGBM rows use a
                    separate table. Requires a live Kite session and host <strong>FavoriteMlAutomation</strong>.
                  </p>
                  <p className="mb-2">
                    <strong>m</strong> is the model candle size (dropdown). <strong>N</strong> is optional minutes between{' '}
                    <strong>new</strong> pass starts; when <strong>N</strong> is set, the server does not wait for the
                    prior <strong>m</strong>-bar to close (still dedupes one pending row per ref bar per engine and validates{' '}
                    <strong>m</strong>-candles). <strong>Intrabar delay</strong> applies only when <strong>N</strong> is blank.
                    The table below loads up to{' '}
                    <strong>{ML_AUTOMATION_RECENT_FETCH_TAKE.toLocaleString()}</strong> merged rows.
                  </p>
                  <p className="mb-0 text-muted" style={{ fontSize: '0.72rem' }}>
                    Server quiet hours (default <strong>Asia/Kolkata</strong>): no <strong>new</strong> auto predictions from{' '}
                    <strong>11:25 PM</strong> through <strong>8:00 AM</strong> daily, and all day <strong>Sat</strong> /{' '}
                    <strong>Sun</strong> (pending rows still resolve). See{' '}
                    <span className="font-monospace">FavoriteMlAutomation:QuietHours*</span> and{' '}
                    <span className="font-monospace">PauseAutomationOnWeekends</span>.
                  </p>
                </div>
              </details>
              <div className="rounded-3 border border-secondary-subtle shadow-sm p-3 mb-3">
                <div className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-3">
                  Table filters
                </div>
                <Row className="g-3 align-items-center mb-3">
                  <Col xs={12} xl="auto">
                    <div className="d-flex flex-wrap align-items-center gap-2">
                      <span className="small text-secondary text-uppercase mb-0 text-nowrap">Direction</span>
                      <ButtonGroup size="sm" aria-label="Filter automation rows by predicted direction">
                        <ToggleButton
                          id={`${automationDirToggleIdPrefix}-up`}
                          type="checkbox"
                          variant={automationDirUp ? 'secondary' : 'outline-secondary'}
                          value="up"
                          checked={automationDirUp}
                          onChange={(e) => setAutomationDirUp(e.currentTarget.checked)}
                        >
                          Up
                        </ToggleButton>
                        <ToggleButton
                          id={`${automationDirToggleIdPrefix}-down`}
                          type="checkbox"
                          variant={automationDirDown ? 'secondary' : 'outline-secondary'}
                          value="down"
                          checked={automationDirDown}
                          onChange={(e) => setAutomationDirDown(e.currentTarget.checked)}
                        >
                          Down
                        </ToggleButton>
                        <ToggleButton
                          id={`${automationDirToggleIdPrefix}-neutral`}
                          type="checkbox"
                          variant={automationDirNeutral ? 'secondary' : 'outline-secondary'}
                          value="neutral"
                          checked={automationDirNeutral}
                          onChange={(e) => setAutomationDirNeutral(e.currentTarget.checked)}
                        >
                          Neutral
                        </ToggleButton>
                      </ButtonGroup>
                    </div>
                  </Col>
                  <Col xs={12} xl="auto">
                    <div className="d-flex flex-wrap align-items-center gap-2">
                      <span className="small text-secondary text-uppercase mb-0 text-nowrap">Outcome</span>
                      <ButtonGroup size="sm" aria-label="Filter automation rows by outcome">
                        <ToggleButton
                          id={`${automationOutcomeToggleIdPrefix}-correct`}
                          type="checkbox"
                          variant={automationOutcomeCorrect ? 'success' : 'outline-success'}
                          value="correct"
                          checked={automationOutcomeCorrect}
                          onChange={(e) => setAutomationOutcomeCorrect(e.currentTarget.checked)}
                        >
                          Correct
                        </ToggleButton>
                        <ToggleButton
                          id={`${automationOutcomeToggleIdPrefix}-wrong`}
                          type="checkbox"
                          variant={automationOutcomeWrong ? 'danger' : 'outline-danger'}
                          value="wrong"
                          checked={automationOutcomeWrong}
                          onChange={(e) => setAutomationOutcomeWrong(e.currentTarget.checked)}
                        >
                          Wrong
                        </ToggleButton>
                        <ToggleButton
                          id={`${automationOutcomeToggleIdPrefix}-pending`}
                          type="checkbox"
                          variant={automationOutcomePending ? 'secondary' : 'outline-secondary'}
                          value="pending"
                          checked={automationOutcomePending}
                          onChange={(e) => setAutomationOutcomePending(e.currentTarget.checked)}
                        >
                          Pending
                        </ToggleButton>
                      </ButtonGroup>
                    </div>
                  </Col>
                  {automationModelsLoading ? (
                    <Col xs={12} xl className="d-flex align-items-center justify-content-xl-end">
                      <span className="small text-muted">
                        <Spinner animation="border" size="sm" className="me-1 align-middle" role="status" />
                        Model registry…
                      </span>
                    </Col>
                  ) : null}
                </Row>
              {automationIntervalsAvailable.length > 0 ? (
                <div className="d-flex flex-wrap align-items-center gap-2 mb-2">
                  <span className="small text-secondary text-uppercase mb-0 text-nowrap pt-1">Interval</span>
                  <div className="d-flex flex-wrap gap-1 align-items-center" role="group" aria-label="Filter by candle interval">
                    {automationIntervalsAvailable.map((iv, ix) => (
                      <ToggleButton
                        key={iv}
                        id={`${automationIntervalToggleIdPrefix}-${ix}`}
                        type="checkbox"
                        size="sm"
                        variant={(automationIntervalOn[iv] ?? true) ? 'secondary' : 'outline-secondary'}
                        value={iv}
                        checked={automationIntervalOn[iv] ?? true}
                        onChange={(e) =>
                          setAutomationIntervalOn((p) => ({ ...p, [iv]: e.currentTarget.checked }))
                        }
                      >
                        {iv}
                      </ToggleButton>
                    ))}
                  </div>
                </div>
              ) : null}
              {automationEngineIdsAvailable.length > 0 ? (
                <div className="d-flex flex-wrap align-items-start gap-2 mb-0">
                  <span className="small text-secondary text-uppercase mb-0 pt-1 text-nowrap">Engine</span>
                  <div className="d-flex flex-wrap gap-1 align-items-center" role="group" aria-label="Filter by registered ML engine">
                    {automationEngineIdsAvailable.map((eng, idx) => {
                      const desc = automationPriceModels?.models?.find((m) => m.id === eng)?.description
                      const short = eng.length > 30 ? `${eng.slice(0, 28)}…` : eng
                      const on = automationEngineOn[eng] ?? true
                      return (
                        <ToggleButton
                          key={eng}
                          id={`${automationEngineToggleIdPrefix}-${idx}`}
                          type="checkbox"
                          size="sm"
                          variant={on ? 'secondary' : 'outline-secondary'}
                          value={eng}
                          checked={on}
                          onChange={(e) =>
                            setAutomationEngineOn((p) => ({ ...p, [eng]: e.currentTarget.checked }))
                          }
                          className="font-monospace"
                          title={desc ? `${eng} — ${desc}` : eng}
                        >
                          <span className="text-truncate d-inline-block" style={{ maxWidth: '11rem' }}>
                            {short}
                          </span>
                        </ToggleButton>
                      )
                    })}
                  </div>
                </div>
              ) : null}
              </div>
              <MlAutomationDirectionVotePie
                rows={automationRecentTableFiltered}
                totalLoaded={automationRecent.length}
              />
              <MlAutomationOutcomesPieGrid
                rows={automationRecentTableFiltered}
                priceModels={automationPriceModels}
              />
              <Row className="align-items-end g-2 mb-2">
                <Col xs={12} md="auto">
                  <div className="small text-secondary text-uppercase mb-0 text-nowrap">Recent auto predictions</div>
                </Col>
                <Col xs={12} md>
                  <Form.Control
                    size="sm"
                    type="search"
                    className="w-100"
                    placeholder="Filter rows (symbol, category, engine, interval, outcome, …)"
                    value={automationTableFilter}
                    onChange={(e) => setAutomationTableFilter(e.target.value)}
                    aria-label="Filter automation prediction rows"
                  />
                </Col>
              </Row>
              <Row className="g-2 mb-2 align-items-end small">
                <Col xs={12} sm={6} md={4} lg={3}>
                  <Form.Group className="mb-0">
                    <Form.Label className="small text-secondary mb-0">Sort column</Form.Label>
                    <Form.Select
                      size="sm"
                      value={automationSortColumn}
                      aria-label="Sort automation table by column"
                      onChange={(e) => {
                        const col = e.target.value as AutomationRecentSortColumn
                        setAutomationSortColumn(col)
                        setAutomationSortHighFirst(
                          col === 'predictedAt' ||
                            col === 'refClose' ||
                            col === 'nextClose' ||
                            col === 'confidence',
                        )
                      }}
                    >
                      <option value="predictedAt">Time (predicted)</option>
                      <option value="symbol">Symbol</option>
                      <option value="category">Category</option>
                      <option value="engineModelId">Engine</option>
                      <option value="interval">Interval</option>
                      <option value="refClose">Ref close</option>
                      <option value="nextClose">Next close</option>
                      <option value="direction">Direction</option>
                      <option value="confidence">Confidence %</option>
                      <option value="outcome">Outcome</option>
                    </Form.Select>
                  </Form.Group>
                </Col>
                <Col xs={12} sm={6} md={3} lg={2}>
                  <Form.Group className="mb-0">
                    <Form.Label className="small text-secondary mb-0">Order</Form.Label>
                    <Form.Select
                      size="sm"
                      value={automationSortHighFirst ? 'high' : 'low'}
                      aria-label="Sort order low to high or high to low"
                      onChange={(e) => setAutomationSortHighFirst(e.target.value === 'high')}
                    >
                      <option value="high">High → low</option>
                      <option value="low">Low → high</option>
                    </Form.Select>
                  </Form.Group>
                </Col>
                <Col xs={6} sm={4} md={2} lg={2}>
                  <Form.Group className="mb-0">
                    <Form.Label className="small text-secondary mb-0">Min conf %</Form.Label>
                    <Form.Control
                      size="sm"
                      type="number"
                      inputMode="numeric"
                      min={0}
                      max={100}
                      placeholder="—"
                      value={automationColFilterConfMin}
                      onChange={(e) => setAutomationColFilterConfMin(e.target.value)}
                      aria-label="Minimum confidence percent"
                    />
                  </Form.Group>
                </Col>
                <Col xs={6} sm={4} md={2} lg={2}>
                  <Form.Group className="mb-0">
                    <Form.Label className="small text-secondary mb-0">Max conf %</Form.Label>
                    <Form.Control
                      size="sm"
                      type="number"
                      inputMode="numeric"
                      min={0}
                      max={100}
                      placeholder="—"
                      value={automationColFilterConfMax}
                      onChange={(e) => setAutomationColFilterConfMax(e.target.value)}
                      aria-label="Maximum confidence percent"
                    />
                  </Form.Group>
                </Col>
                <Col xs={12} sm={4} md={3} lg={3}>
                  <Form.Group className="mb-0">
                    <Form.Label className="small text-secondary mb-0">Category</Form.Label>
                    <Form.Select
                      size="sm"
                      value={automationColFilterCategory}
                      aria-label="Filter by category"
                      onChange={(e) => setAutomationColFilterCategory(e.target.value)}
                    >
                      <option value="">All categories</option>
                      {automationCategoryFilterOptions.map((lab) => (
                        <option key={lab} value={lab}>
                          {lab}
                        </option>
                      ))}
                    </Form.Select>
                  </Form.Group>
                </Col>
              </Row>
              {automationRecent.length > 0 ? (
                <div className="small text-muted mb-2" style={{ fontSize: '0.72rem' }}>
                  Table: <strong>{automationRecentTableRows.length}</strong> row(s)
                  {automationRecentTableFiltered.length !== automationRecentToolbarFiltered.length ? (
                    <>
                      {' '}
                      (<strong>{automationRecentTableFiltered.length}</strong> after column filters)
                    </>
                  ) : null}
                  {' · '}
                  <strong>{automationRecentToolbarFiltered.length}</strong> after search &amp; toggle filters ·{' '}
                  <strong>{automationRecent.length}</strong> in loaded range. Sort: column headers or Sort column /
                  Order (low→high / high→low).
                </div>
              ) : null}
              <div
                className="table-responsive"
                style={{
                  maxHeight: ML_AUTOMATION_TABLE_MAX_HEIGHT,
                  overflowY: 'auto',
                }}
              >
                <Table striped bordered size="sm" className="mb-0 align-middle">
                  <thead className="table-light">
                    <tr className="text-nowrap">
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'predictedAt'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Sort by prediction time · click again to reverse"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'predictedAt',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Time
                        {automationSortColumn === 'predictedAt' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'symbol'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Sort by symbol"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'symbol',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Symbol
                        {automationSortColumn === 'symbol' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'category'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Browse-tab category from the row's exchange (NFO/BFO→F&amp;O, MCX→Commodities, NSE/BSE→Spot)"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'category',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Category
                        {automationSortColumn === 'category' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'engineModelId'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Sort by engine model id"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'engineModelId',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Engine
                        {automationSortColumn === 'engineModelId' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'interval'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Sort by bar size (chart order)"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'interval',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Interval
                        {automationSortColumn === 'interval' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'refClose'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Close of the reference bar when the prediction ran"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'refClose',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Ref close
                        {automationSortColumn === 'refClose' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'nextClose'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Close of the next bar used to score the prediction (pending rows sort last)"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'nextClose',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Next close
                        {automationSortColumn === 'nextClose' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'direction'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Sort: down · neutral · up"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'direction',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Dir
                        {automationSortColumn === 'direction' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'confidence'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Engine confidence for this prediction (0–100%)"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'confidence',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Conf %
                        {automationSortColumn === 'confidence' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                      <th
                        role="columnheader"
                        aria-sort={
                          automationSortColumn === 'outcome'
                            ? automationSortHighFirst
                              ? 'descending'
                              : 'ascending'
                            : undefined
                        }
                        className="user-select-none"
                        style={{ cursor: 'pointer' }}
                        title="Sort: pending · wrong · correct"
                        onClick={() =>
                          sortColumnHeaderClick(
                            'outcome',
                            automationSortColumn,
                            setAutomationSortColumn,
                            setAutomationSortHighFirst,
                          )
                        }
                      >
                        Outcome
                        {automationSortColumn === 'outcome' ? (
                          <span className="ms-1 text-primary">{automationSortHighFirst ? '↓' : '↑'}</span>
                        ) : null}
                      </th>
                    </tr>
                  </thead>
                  <tbody className="font-monospace">
                    {automationRecent.length === 0 && !automationRecentLoading ? (
                      <tr>
                        <td colSpan={10} className="text-secondary small">
                          No automation rows yet.
                        </td>
                      </tr>
                    ) : automationRecentToolbarFiltered.length === 0 && automationRecent.length > 0 ? (
                      <tr>
                        <td colSpan={10} className="text-secondary small fst-italic">
                          No rows match search or toggle filters above.
                        </td>
                      </tr>
                    ) : automationRecentTableFiltered.length === 0 &&
                      automationRecentToolbarFiltered.length > 0 ? (
                      <tr>
                        <td colSpan={10} className="text-secondary small fst-italic">
                          No rows match column filters (confidence range / category). Clear min/max/category to see
                          rows.
                        </td>
                      </tr>
                    ) : (
                      automationRecentTableRows.map((r) => {
                        const category = automationRowCategory(r, favoriteByInstrumentToken)
                        return (
                        <tr key={r.id}>
                          <td className="small">{formatLocalDateTime(r.predictedAt)}</td>
                          <td title={`Instrument token ${r.instrumentToken}`}>
                            {formatMlAutomationSymbol(r, favoriteByInstrumentToken)}
                          </td>
                          <td
                            className="small"
                            title={
                              category.exchange
                                ? `Exchange: ${category.exchange}`
                                : 'Exchange unknown for this row'
                            }
                          >
                            {category.label}
                          </td>
                          <td
                            className="text-truncate small"
                            style={{ maxWidth: '8rem' }}
                            title={`Engine: ${r.engineModelId}`}
                          >
                            {r.engineModelId}
                          </td>
                          <td>{r.interval}</td>
                          <td title={`Reference bar: ${formatLocalDateTime(r.refBarTime)}`}>
                            {Number.isFinite(r.refClose) ? r.refClose.toFixed(4) : '—'}
                          </td>
                          <td title={r.nextBarTime ? `Next bar: ${formatLocalDateTime(r.nextBarTime)}` : 'Pending'}>
                            {r.nextClose != null && Number.isFinite(r.nextClose) ? r.nextClose.toFixed(4) : '—'}
                          </td>
                          <td>{r.direction}</td>
                          <td>{r.confidence}%</td>
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
                        )
                      })
                    )}
                  </tbody>
                </Table>
              </div>
            </div>
            ) : mainTab === 'manualTrade' ? (
            <div className="mt-3 d-flex flex-column gap-3">
              {!isZerodha ? (
                <Alert variant="secondary" className="py-2 small mb-0 border border-secondary-subtle shadow-sm">
                  <span className="fw-semibold text-body">Kite session required.</span> Connect{' '}
                  <strong>Zerodha</strong> to use manual paper trade (wallet at Kite LTP × lock lot; no orders).
                </Alert>
              ) : null}
              <p className="small text-secondary mb-0">
                <strong>Scalper view</strong>: live candles and LTP ticks for whichever lock you select — the same instrument
                is used for manual paper buy/sell below.
              </p>
              <ManualTradeScalperView
                isZerodha={isZerodha}
                tradingLocks={tradingLocks}
                selectedInstrumentToken={demoPaperToken}
                onSelectedInstrumentTokenChange={setDemoPaperToken}
                paperLastBuyPrice={manualTradePaperLastBuyPrice}
                chartFullscreenToolbar={
                  <ChartSettingsToolbar
                    idPrefix="manual-trade-scalper"
                    rangePreset={chartRangePreset}
                    onRangePresetChange={setChartRangePreset}
                    interval={chartIntervalByToken[demoPaperToken.trim()] ?? chartInterval}
                    onIntervalChange={(iv) => persistInstrumentChartInterval(demoPaperToken.trim(), iv)}
                    trendAnalysisSelections={trendAnalysisSelections}
                    onTrendAnalysisSelectionsChange={setTrendAnalysisSelections}
                    graphType={chartGraphType}
                    onGraphTypeChange={setChartGraphType}
                    maLineVisibility={maLineVisibility}
                    onMaLineVisibilityChange={patchMaLineVisibility}
                    customEmaPeriod={customEmaPeriod}
                    onCustomEmaPeriodChange={setCustomEmaPeriod}
                  />
                }
                kiteChart={{
                  rangePreset: chartRangePreset,
                  interval: chartIntervalByToken[demoPaperToken.trim()] ?? chartInterval,
                  graphType: chartGraphType,
                  maLineVisibility,
                  customEmaPeriod,
                  chartZoomStored: chartZoomByToken[demoPaperToken.trim()] ?? null,
                  onChartZoomStoredChange: (stored) => persistInstrumentChartZoom(demoPaperToken.trim(), stored),
                  demoPaperBuyMarkers: demoPaperOpenBuysByInstrumentToken[demoPaperToken.trim()] ?? [],
                }}
              />
              <ManualPaperTradePanel
                heading={<h2 className="h6 text-body mb-2">Manual paper trade</h2>}
                intro={
                  <p className="small text-secondary mb-0">
                    Each locked row: <strong>Buy</strong> debits the wallet, <strong>Sell</strong> credits and closes open longs
                    (Kite <strong>LTP × lot size × lots</strong>). Use one <strong>Lots</strong> field for Buy and Sell on every row (API field{' '}
                    <span className="font-monospace">contracts</span> = lots). No broker orders.
                    Add locks on <Link to="/instruments?tab=locked">Locked for trading</Link>.
                  </p>
                }
                showWalletLine
                walletBalanceInr={demoAutoTradeNotionalInr}
                isZerodha={isZerodha}
                tradingLocks={tradingLocks}
                demoPaperToken={demoPaperToken}
                setDemoPaperToken={setDemoPaperToken}
                demoPaperContracts={demoPaperContracts}
                setDemoPaperContracts={setDemoPaperContracts}
                demoPaperTradeBusy={demoPaperTradeBusy}
                demoPaperTradeError={demoPaperTradeError}
                demoPaperTradeLast={demoPaperTradeLast}
                demoPaperPositions={demoPaperPositions}
                demoPaperPositionsLoading={demoPaperPositionsLoading}
                demoPaperPositionsError={demoPaperPositionsError}
                demoPaperTrades={demoPaperTrades}
                demoPaperTradesLoading={demoPaperTradesLoading}
                demoPaperTradesError={demoPaperTradesError}
                executeDemoPaperTrade={executeDemoPaperTrade}
                loadDemoPaperPositions={loadDemoPaperPositions}
                loadDemoPaperTrades={loadDemoPaperTrades}
              />
            </div>
            ) : mainTab === 'autoTrading' ? (
            <div className="mt-3 d-flex flex-column gap-3">
              {!isZerodha ? (
                <Alert variant="secondary" className="py-2 small mb-0 border border-secondary-subtle shadow-sm">
                  <span className="fw-semibold text-body">Kite session required.</span> Connect{' '}
                  <strong>Zerodha</strong> to configure hypothetical demo P&amp;L.
                </Alert>
              ) : null}
              <div className="border-bottom border-secondary-subtle pb-3 mb-1">
                <h2 className="h6 text-body mb-1">Demo auto-trade</h2>
                <p className="small text-secondary mb-0">
                  Hypothetical EOD and multi-day reports use automation rows for instruments in{' '}
                  <strong>Locked for trading</strong> ({tradingLocks.length} saved lock{tradingLocks.length === 1 ? '' : 's'}).
                  Add or remove locks on that tab; refresh EOD after changing locks.
                </p>
              </div>

              <div className="rounded-3 border border-info border-opacity-25 shadow-sm overflow-hidden">
                <div className="px-3 py-2 border-bottom border-info border-opacity-25 bg-body-secondary d-flex flex-wrap align-items-center justify-content-between gap-2">
                  <span className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-0">
                    Demo auto-trade
                  </span>
                  <div className="d-flex flex-wrap gap-1 align-items-center">
                    <Badge bg="info" pill className="small">
                      No live orders
                    </Badge>
                    <Badge bg="secondary" pill className="small">
                      Locked symbols only
                    </Badge>
                  </div>
                </div>
                <div className="p-3 p-md-4 bg-body-tertiary bg-opacity-25">
                  <Row className="g-3 align-items-start">
                    <Col xs={12} md>
                      <Form.Check
                        type="switch"
                        id="demo-auto-trade-switch"
                        className="mb-2"
                        label={
                          <span className="fw-semibold">
                            Enable demo auto-trade
                            <span className="d-block small fw-normal text-secondary mt-1 lh-sm">
                              Marks intent only. Allocation uses your <strong>Wallet</strong> balance{' '}
                              <strong>{formatInrRupee(demoAutoTradeNotionalInr)}</strong> — hypothetical same-day gross
                              and net P&amp;L from automation rows for instruments in <strong>Locked for trading</strong> only (server filters by lock tokens; host{' '}
                              <span className="font-monospace">DemoAutoTrade:Charges</span>{' '}
                              applies flat + turnover fees when enabled). Pick an allocation preset below.
                            </span>
                          </span>
                        }
                        checked={demoAutoTradeEnabled}
                        disabled={demoAutoTradeSaving}
                        onChange={(e) => {
                          void persistDemoAutoTrade(e.target.checked, demoAutoTradeStrategy)
                        }}
                      />
                      <Form.Group className="mt-2 mb-0" controlId="demo-auto-trade-strategy">
                        <Form.Label className="small text-secondary mb-1">Allocation preset (hypothetical)</Form.Label>
                        <Form.Select
                          size="sm"
                          value={demoAutoTradeStrategy}
                          disabled={demoAutoTradeSaving}
                          aria-label="Demo auto-trade allocation strategy"
                          onChange={(e) => {
                            const next = e.target.value
                            void persistDemoAutoTrade(demoAutoTradeEnabled, next)
                          }}
                        >
                          {DEMO_AUTO_TRADE_STRATEGY_OPTIONS.map((o) => (
                            <option key={o.id} value={o.id}>
                              {o.label}
                            </option>
                          ))}
                        </Form.Select>
                        <Form.Text className="text-muted d-block" style={{ fontSize: '0.7rem' }}>
                          {
                            DEMO_AUTO_TRADE_STRATEGY_OPTIONS.find((o) => o.id === demoAutoTradeStrategy)
                              ?.hint
                          }{' '}
                          Not financial advice; no live orders.
                        </Form.Text>
                      </Form.Group>
                    </Col>
                    <Col xs={12} md="auto" className="d-flex flex-wrap gap-2 align-items-start justify-content-md-end">
                      <Button
                        type="button"
                        variant="outline-primary"
                        size="sm"
                        disabled={demoEodLoading}
                        onClick={() => void loadDemoEodSummary()}
                      >
                        {demoEodLoading ? 'Loading…' : 'Refresh EOD summary'}
                      </Button>
                    </Col>
                  </Row>
                  <div className="mt-3 pt-3 border-top border-secondary-subtle">
                    <ManualPaperTradePanel
                      heading={
                        <h6 className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-2">
                          Manual paper buy / sell
                        </h6>
                      }
                      intro={
                        <p className="small text-secondary mb-2">
                          Table of locks with row <strong>Buy</strong>/<strong>Sell</strong> — same as <strong>Manual trade</strong>.{' '}
                          <Link to="/instruments?tab=manual-trade">Open full tab</Link>.
                        </p>
                      }
                      showWalletLine={false}
                      walletBalanceInr={demoAutoTradeNotionalInr}
                      isZerodha={isZerodha}
                      tradingLocks={tradingLocks}
                      demoPaperToken={demoPaperToken}
                      setDemoPaperToken={setDemoPaperToken}
                      demoPaperContracts={demoPaperContracts}
                      setDemoPaperContracts={setDemoPaperContracts}
                      demoPaperTradeBusy={demoPaperTradeBusy}
                      demoPaperTradeError={demoPaperTradeError}
                      demoPaperTradeLast={demoPaperTradeLast}
                      demoPaperPositions={demoPaperPositions}
                      demoPaperPositionsLoading={demoPaperPositionsLoading}
                      demoPaperPositionsError={demoPaperPositionsError}
                      demoPaperTrades={demoPaperTrades}
                      demoPaperTradesLoading={demoPaperTradesLoading}
                      demoPaperTradesError={demoPaperTradesError}
                      executeDemoPaperTrade={executeDemoPaperTrade}
                      loadDemoPaperPositions={loadDemoPaperPositions}
                      loadDemoPaperTrades={loadDemoPaperTrades}
                    />
                  </div>
                  <div className="mt-3 pt-3 border-top border-secondary-subtle">
                    <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
                      <h6 className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-0">
                        Today&apos;s hypothetical legs (live)
                      </h6>
                      <div className="d-flex flex-wrap align-items-center gap-2 small text-muted">
                        {demoTodayLegsLoading ? <span>Refreshing…</span> : null}
                        {demoTodayLegs ? (
                          <span className="font-monospace" title="Last server snapshot (UTC)">
                            {demoTodayLegs.generatedAtUtc.slice(0, 19)}Z
                          </span>
                        ) : null}
                        <Button
                          type="button"
                          variant="outline-secondary"
                          size="sm"
                          disabled={demoTodayLegsLoading || !isZerodha}
                          onClick={() => void loadDemoTodayLegs()}
                        >
                          Refresh now
                        </Button>
                      </div>
                    </div>
                    <p className="small text-secondary mb-2">
                      Same calendar day and <strong>Locked for trading</strong> filter as EOD; rows refresh about every{' '}
                      <strong>12s</strong> while <strong>Demo auto-trade</strong> is open. Hypothetical P&amp;L uses Kite{' '}
                      <strong>lot sizes</strong> saved on locks: each leg&apos;s INR slice is floored to whole lots using
                      next open→next close (when present) vs ref close→next close; <strong>Buy</strong> / <strong>Sell</strong>{' '}
                      follow long vs short semantics; gross ₹ is (sell − buy) × lot size × lots for long/up and the inverse for
                      short/down (before illustrative fees).
                    </p>
                    {demoTodayLegsError ? (
                      <Alert variant="warning" className="py-2 small mb-2">
                        {demoTodayLegsError}
                      </Alert>
                    ) : null}
                    {demoTodayLegsPnlChartRows.length > 0 ? (
                      <div className="mb-3 border border-secondary-subtle rounded p-2 bg-body-tertiary">
                        <div className="small fw-semibold text-secondary mb-2">
                          Allocated legs — net P&amp;L (hypothetical)
                        </div>
                        <div style={{ height: 'min(14rem, 32vh)', minHeight: '11rem' }}>
                          <ChartWithRightGutter>
                          <ResponsiveContainer width="100%" height="100%" debounce={50}>
                            <BarChart data={demoTodayLegsPnlChartRows} margin={{ top: 4, right: 8, left: 4, bottom: 4 }}>
                              <CartesianGrid strokeDasharray="3 3" stroke="#49505733" />
                              <XAxis
                                dataKey="symbolLabel"
                                stroke="#adb5bd"
                                tick={{ fontSize: 9 }}
                                interval={0}
                                angle={demoTodayLegsPnlChartRows.length > 6 ? -28 : 0}
                                textAnchor={demoTodayLegsPnlChartRows.length > 6 ? 'end' : 'middle'}
                                height={demoTodayLegsPnlChartRows.length > 6 ? 52 : 28}
                              />
                              <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} width={52} />
                              <Tooltip
                                formatter={(value: number) => formatInrRupee(value)}
                                labelFormatter={(_, payload) =>
                                  payload?.[0]?.payload?.symbolFull != null
                                    ? String(payload[0].payload.symbolFull)
                                    : ''
                                }
                                contentStyle={{
                                  background: '#212529',
                                  border: '1px solid #495057',
                                  borderRadius: 8,
                                  fontSize: 12,
                                }}
                              />
                              <ReferenceLine y={0} stroke="#6c757d" strokeDasharray="4 4" />
                              <Bar dataKey="netPnl" name="Net" maxBarSize={36} radius={[2, 2, 0, 0]}>
                                {demoTodayLegsPnlChartRows.map((r) => (
                                  <Cell key={r.key} fill={r.netPnl >= 0 ? '#198754' : '#dc3545'} />
                                ))}
                              </Bar>
                            </BarChart>
                          </ResponsiveContainer>
                          </ChartWithRightGutter>
                        </div>
                      </div>
                    ) : null}
                    <div
                      className="table-responsive border border-secondary-subtle rounded mb-0"
                      style={{ maxHeight: 'min(420px, 55vh)', overflowY: 'auto' }}
                    >
                      <Table size="sm" striped bordered hover className="mb-0 small align-middle">
                        <thead className="table-light text-nowrap">
                          <tr>
                            <th>Time</th>
                            <th>Symbol</th>
                            <th>Dir</th>
                            <th className="text-end">Conf</th>
                            <th>Outcome</th>
                            <th>Status</th>
                            <th className="text-end">Lots</th>
                            <th className="text-end">Buy</th>
                            <th className="text-end">Sell</th>
                            <th className="text-end">Exposure</th>
                            <th className="text-end">Alloc slice</th>
                            <th className="text-end">Gross</th>
                            <th className="text-end">Fees</th>
                            <th className="text-end">Net</th>
                            <th>Engine</th>
                          </tr>
                        </thead>
                        <tbody className="font-monospace">
                          {!demoTodayLegs || demoTodayLegs.legs.length === 0 ? (
                            <tr>
                              <td colSpan={15} className="text-secondary small">
                                {demoTodayLegsLoading
                                  ? 'Loading legs…'
                                  : 'No demo legs for today (add trading locks or wait for automation rows).'}
                              </td>
                            </tr>
                          ) : (
                            demoTodayLegs.legs.map((leg) => {
                              const fav = favoriteByInstrumentToken.get(leg.instrumentToken)
                              const sym = leg.tradingsymbol?.trim()
                                ? leg.exchange?.trim()
                                  ? `${leg.tradingsymbol.trim()} (${leg.exchange.trim()})`
                                  : leg.tradingsymbol.trim()
                                : fav
                                  ? `${fav.tradingsymbol} (${fav.exchange})`
                                  : leg.instrumentToken
                              return (
                                <tr key={leg.predictionId}>
                                  <td className="small">{formatLocalDateTime(leg.predictedAtUtc)}</td>
                                  <td className="small" title={`Token ${leg.instrumentToken}`}>
                                    {sym}
                                  </td>
                                  <td>{leg.direction}</td>
                                  <td className="text-end">{leg.confidence}%</td>
                                  <td
                                    className={
                                      leg.outcome === 'correct'
                                        ? 'text-success'
                                        : leg.outcome === 'wrong'
                                          ? 'text-danger'
                                          : 'text-muted'
                                    }
                                  >
                                    {leg.outcome}
                                  </td>
                                  <td className="small text-wrap" style={{ maxWidth: '10rem' }}>
                                    <span
                                      className={
                                        leg.status === 'allocated'
                                          ? 'text-success'
                                          : leg.status === 'pending'
                                            ? 'text-warning'
                                            : 'text-secondary'
                                      }
                                    >
                                      {formatDemoAutoTradeLegStatus(leg.status)}
                                    </span>
                                    {leg.statusDetail ? (
                                      <span className="d-block text-muted" style={{ fontSize: '0.68rem' }}>
                                        {leg.statusDetail}
                                      </span>
                                    ) : null}
                                  </td>
                                  <td className="text-end text-nowrap font-monospace" title="Lots × lot size × price">
                                    {(leg.instrumentLotMultiplier ?? 0) > 0 && (leg.demoWholeLotsTraded ?? 0) > 0
                                      ? `${leg.demoWholeLotsTraded}×${leg.instrumentLotMultiplier}`
                                      : '—'}
                                  </td>
                                  <td className="text-end text-nowrap">
                                    {formatDemoHypotheticalPrice(leg.hypotheticalBuyPrice)}
                                  </td>
                                  <td className="text-end text-nowrap">
                                    {formatDemoHypotheticalPrice(leg.hypotheticalSellPrice)}
                                  </td>
                                  <td className="text-end">
                                    {leg.status === 'allocated' && leg.committedExposureApproxInr > 0
                                      ? formatInrRupee(leg.committedExposureApproxInr)
                                      : '—'}
                                  </td>
                                  <td className="text-end">
                                    {leg.allocatedNotionalInr > 0 ? formatInrRupee(leg.allocatedNotionalInr) : '—'}
                                  </td>
                                  <td className="text-end">
                                    {leg.status === 'allocated' ? formatInrRupee(leg.legGrossPnlInr) : '—'}
                                  </td>
                                  <td className="text-end">
                                    {leg.status === 'allocated' ? formatInrRupee(leg.legFeesInr) : '—'}
                                  </td>
                                  <td
                                    className={`text-end fw-semibold ${
                                      leg.legNetPnlInr > 0
                                        ? 'text-success'
                                        : leg.legNetPnlInr < 0
                                          ? 'text-danger'
                                          : ''
                                    }`}
                                  >
                                    {leg.status === 'allocated' ? formatInrRupee(leg.legNetPnlInr) : '—'}
                                  </td>
                                  <td
                                    className="small text-truncate"
                                    style={{ maxWidth: '7rem' }}
                                    title={leg.engineModelId}
                                  >
                                    {leg.engineModelId}
                                  </td>
                                </tr>
                              )
                            })
                          )}
                        </tbody>
                      </Table>
                    </div>
                    {demoTodayLegs?.mayBeTruncated ? (
                      <p className="text-warning small mb-0 mt-2" style={{ fontSize: '0.72rem' }}>
                        Automation fetch hit row cap; legs may omit older signals for this day.
                      </p>
                    ) : null}
                  </div>
                  {demoEodError ? (
                    <Alert variant="warning" className="py-2 small mt-3 mb-0">
                      {demoEodError}
                    </Alert>
                  ) : null}
                  {demoEodSummary ? (
                    <div className="mt-3 pt-3 border-top border-secondary-subtle">
                      <h6 className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-3">
                        End-of-day outcome (hypothetical)
                      </h6>
                      <Row className="g-2 small">
                        <Col xs={6} sm={4} className="text-secondary">
                          Report day
                        </Col>
                        <Col xs={6} sm={8} className="font-monospace">
                          {demoEodSummary.reportDateIst}{' '}
                          <span className="text-muted">({demoEodSummary.reportTimeZoneId})</span>
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Demo notional
                        </Col>
                        <Col xs={6} sm={8}>
                          {formatInrRupee(demoEodSummary.demoNotionalInr)}
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Preset
                        </Col>
                        <Col xs={6} sm={8}>{demoEodSummary.demoAutoTradeStrategyTitle}</Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Locked instruments (demo)
                        </Col>
                        <Col xs={6} sm={8}>
                          {demoEodSummary.demoAutoTradeLockedInstrumentCount ?? 0}
                          {(demoEodSummary.demoAutoTradeLockedInstrumentCount ?? 0) === 0 ? (
                            <span className="text-warning ms-1 small">
                              No locks — server uses zero automation rows for this demo.
                            </span>
                          ) : null}
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Signals (day)
                        </Col>
                        <Col xs={6} sm={8}>
                          {demoEodSummary.totalSignals}
                          {demoEodSummary.mayBeTruncated ? (
                            <span className="text-warning ms-1" title="Loaded row cap reached; counts may be incomplete.">
                              (may be truncated)
                            </span>
                          ) : null}
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Pending / directional (priced)
                        </Col>
                        <Col xs={6} sm={8}>
                          {demoEodSummary.pendingSignals} / {demoEodSummary.directionalTradeableLegs}
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Allocated legs
                        </Col>
                        <Col xs={6} sm={8}>{demoEodSummary.allocatedLegsForPnl}</Col>
                        {demoEodSummary.skippedLowConfidenceLegs > 0 ? (
                          <>
                            <Col xs={6} sm={4} className="text-secondary">
                              Below confidence cutoff
                            </Col>
                            <Col xs={6} sm={8}>{demoEodSummary.skippedLowConfidenceLegs}</Col>
                          </>
                        ) : null}
                        <Col xs={6} sm={4} className="text-secondary">
                          Correct / wrong
                        </Col>
                        <Col xs={6} sm={8}>
                          {demoEodSummary.correctOutcomes} / {demoEodSummary.wrongOutcomes}
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Fee model (host)
                        </Col>
                        <Col xs={6} sm={8}>
                          {demoEodSummary.demoAutoTradeChargesEnabled ? (
                            <span>
                              On — {formatInrRupee(demoEodSummary.demoAutoTradeRoundTripFlatInrPerLeg)} / leg +{' '}
                              {demoEodSummary.demoAutoTradeRoundTripTurnoverBps} bps turnover
                            </span>
                          ) : (
                            <span className="text-muted">Off (gross = net)</span>
                          )}
                        </Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Gross P&amp;L
                        </Col>
                        <Col xs={6} sm={8}>{formatInrRupee(demoEodSummary.hypotheticalGrossPnlInr)}</Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Est. charges
                        </Col>
                        <Col xs={6} sm={8}>{formatInrRupee(demoEodSummary.hypotheticalChargesInr)}</Col>
                        <Col xs={6} sm={4} className="text-secondary">
                          Net P&amp;L (after fees)
                        </Col>
                        <Col xs={6} sm={8}>
                          <span
                            className={
                              demoEodSummary.hypotheticalTotalPnlInr > 0
                                ? 'text-success fw-semibold'
                                : demoEodSummary.hypotheticalTotalPnlInr < 0
                                  ? 'text-danger fw-semibold'
                                  : 'text-body-secondary'
                            }
                          >
                            {formatInrRupee(demoEodSummary.hypotheticalTotalPnlInr)}
                          </span>
                        </Col>
                      </Row>
                      <p className="text-muted mb-0 mt-2" style={{ fontSize: '0.72rem' }}>
                        {demoEodSummary.pnlAllocationNote}
                      </p>
                    </div>
                  ) : null}

                  <div className="mt-4 pt-3 border-top border-secondary-subtle">
                    <h6 className="small fw-semibold text-uppercase text-secondary letter-spacing-1 mb-2">
                      Full demo auto-trade report
                    </h6>
                    <p className="small text-secondary mb-2">
                      Per-day hypothetical P&amp;L (same rules as EOD), totals, direction mix, and outcomes by engine /
                      interval — <strong>locked instruments only</strong>. Default window is the last <strong>7</strong> calendar days in the server report timezone;
                      or reuse <strong>From / To</strong> from <strong>Merged log range &amp; email</strong> on{' '}
                      <strong>Auto predictions</strong> (same browser session).
                    </p>
                    <div className="d-flex flex-wrap gap-2 mb-2">
                      <Button
                        type="button"
                        variant="outline-primary"
                        size="sm"
                        disabled={demoFullReportLoading || !isZerodha}
                        onClick={() => void loadDemoFullReport('seven')}
                      >
                        {demoFullReportLoading ? 'Loading…' : 'Load (last 7 days)'}
                      </Button>
                      <Button
                        type="button"
                        variant="outline-primary"
                        size="sm"
                        disabled={demoFullReportLoading || !isZerodha}
                        onClick={() => void loadDemoFullReport('merged')}
                      >
                        {demoFullReportLoading ? 'Loading…' : 'Load (merged log range)'}
                      </Button>
                      {demoFullReport ? (
                        <Button
                          type="button"
                          variant="outline-secondary"
                          size="sm"
                          disabled={!demoFullReport}
                          onClick={() => {
                            const blob = new Blob([JSON.stringify(demoFullReport, null, 2)], {
                              type: 'application/json',
                            })
                            const a = document.createElement('a')
                            a.href = URL.createObjectURL(blob)
                            a.download = `demo-auto-trade-full-report-${demoFullReport.generatedAtUtc.slice(0, 19).replace(/[:T]/g, '-')}.json`
                            a.click()
                            URL.revokeObjectURL(a.href)
                          }}
                        >
                          Download JSON
                        </Button>
                      ) : null}
                    </div>
                    {demoFullReportError ? (
                      <Alert variant="warning" className="py-2 small mb-2">
                        {demoFullReportError}
                      </Alert>
                    ) : null}
                    {demoFullReport ? (
                      <div className="small">
                        <p className="text-muted mb-2" style={{ fontSize: '0.72rem' }}>
                          {demoFullReport.disclaimer}{' '}
                          {demoFullReport.mayBeTruncated ? (
                            <span className="text-warning">Row cap reached; counts may be incomplete.</span>
                          ) : null}
                        </p>
                        <Row className="g-2 mb-2">
                          <Col xs={6} sm={4} className="text-secondary">
                            Range
                          </Col>
                          <Col xs={6} sm={8} className="font-monospace">
                            {demoFullReport.reportRangeSummary}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Generated (UTC)
                          </Col>
                          <Col xs={6} sm={8} className="font-monospace">
                            {demoFullReport.generatedAtUtc}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Demo / automation flags
                          </Col>
                          <Col xs={6} sm={8}>
                            demo {demoFullReport.demoAutoTradeEnabled ? 'on' : 'off'} · favorites ML{' '}
                            {demoFullReport.favoriteMlAutomationEnabled ? 'on' : 'off'}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Locked instruments (demo)
                          </Col>
                          <Col xs={6} sm={8}>
                            {demoFullReport.demoAutoTradeLockedInstrumentCount ?? 0}
                            {(demoFullReport.demoAutoTradeLockedInstrumentCount ?? 0) === 0 ? (
                              <span className="text-warning ms-1 small">No locks in scope for demo math.</span>
                            ) : null}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Preset / notional per day
                          </Col>
                          <Col xs={6} sm={8}>
                            {demoFullReport.demoAutoTradeStrategyTitle} ·{' '}
                            {formatInrRupee(demoFullReport.demoNotionalInrPerDay)}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Fee model (host)
                          </Col>
                          <Col xs={6} sm={8}>
                            {demoFullReport.demoAutoTradeChargesEnabled ? (
                              <span>
                                On — {formatInrRupee(demoFullReport.demoAutoTradeRoundTripFlatInrPerLeg)} / leg +{' '}
                                {demoFullReport.demoAutoTradeRoundTripTurnoverBps} bps
                              </span>
                            ) : (
                              <span className="text-muted">Off</span>
                            )}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Signals (range)
                          </Col>
                          <Col xs={6} sm={8}>{demoFullReport.totalSignalsInRange}</Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Pending / correct / wrong
                          </Col>
                          <Col xs={6} sm={8}>
                            {demoFullReport.pendingSignalsInRange} / {demoFullReport.correctOutcomesInRange} /{' '}
                            {demoFullReport.wrongOutcomesInRange}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Direction up / down / neutral
                          </Col>
                          <Col xs={6} sm={8}>
                            {demoFullReport.directionCountUp} / {demoFullReport.directionCountDown} /{' '}
                            {demoFullReport.directionCountNeutral}
                          </Col>
                          <Col xs={6} sm={4} className="text-secondary">
                            Σ gross / charges / net
                          </Col>
                          <Col xs={6} sm={8}>
                            {formatInrRupee(demoFullReport.hypotheticalGrossPnlInrSummedDays)} /{' '}
                            {formatInrRupee(demoFullReport.hypotheticalChargesInrSummedDays)} /{' '}
                            <span
                              className={
                                demoFullReport.hypotheticalTotalPnlInrSummedDays > 0
                                  ? 'text-success fw-semibold'
                                  : demoFullReport.hypotheticalTotalPnlInrSummedDays < 0
                                    ? 'text-danger fw-semibold'
                                    : 'text-body-secondary'
                              }
                            >
                              {formatInrRupee(demoFullReport.hypotheticalTotalPnlInrSummedDays)}
                            </span>
                            <span className="text-muted ms-1">
                              ({demoFullReport.directionalTradeableLegsInRange} directional leg-days)
                            </span>
                          </Col>
                        </Row>
                        {demoFullReport.dailySummaries.length > 0 ? (
                          <>
                            <div className="fw-semibold text-secondary mb-1 mt-2">Per calendar day</div>
                            {demoFullReportPnlChartRows.length > 0 ? (
                              <Row className="g-3 mb-3">
                                <Col xs={12} lg={6}>
                                  <div className="small text-secondary mb-1">Daily net P&amp;L</div>
                                  <div
                                    className="border border-secondary-subtle rounded p-2 bg-body-tertiary"
                                    style={{ height: '14rem' }}
                                  >
                                    <ChartWithRightGutter>
                                      <ResponsiveContainer width="100%" height="100%" debounce={50}>
                                      <BarChart
                                        data={demoFullReportPnlChartRows}
                                        margin={{ top: 8, right: 8, left: 4, bottom: 4 }}
                                      >
                                        <CartesianGrid strokeDasharray="3 3" stroke="#49505733" />
                                        <XAxis dataKey="dayLabel" stroke="#adb5bd" tick={{ fontSize: 10 }} />
                                        <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} width={52} />
                                        <Tooltip
                                          formatter={(value: number, name: string) => [
                                            formatInrRupee(value),
                                            name === 'netPnl'
                                              ? 'Net'
                                              : name === 'grossPnl'
                                                ? 'Gross'
                                                : 'Fees',
                                          ]}
                                          labelFormatter={(_, payload) =>
                                            payload?.[0]?.payload?.reportDate != null
                                              ? String(payload[0].payload.reportDate)
                                              : ''
                                          }
                                          contentStyle={{
                                            background: '#212529',
                                            border: '1px solid #495057',
                                            borderRadius: 8,
                                            fontSize: 12,
                                          }}
                                        />
                                        <ReferenceLine y={0} stroke="#6c757d" strokeDasharray="4 4" />
                                        <Bar dataKey="netPnl" name="netPnl" maxBarSize={40} radius={[2, 2, 0, 0]}>
                                          {demoFullReportPnlChartRows.map((r) => (
                                            <Cell
                                              key={r.reportDate}
                                              fill={r.netPnl >= 0 ? '#198754' : '#dc3545'}
                                            />
                                          ))}
                                        </Bar>
                                      </BarChart>
                                    </ResponsiveContainer>
                                    </ChartWithRightGutter>
                                  </div>
                                </Col>
                                <Col xs={12} lg={6}>
                                  <div className="small text-secondary mb-1">Cumulative net P&amp;L</div>
                                  <div
                                    className="border border-secondary-subtle rounded p-2 bg-body-tertiary"
                                    style={{ height: '14rem' }}
                                  >
                                    <ChartWithRightGutter>
                                    <ResponsiveContainer width="100%" height="100%" debounce={50}>
                                      <LineChart
                                        data={demoFullReportPnlChartRows}
                                        margin={{ top: 8, right: 8, left: 4, bottom: 4 }}
                                      >
                                        <CartesianGrid strokeDasharray="3 3" stroke="#49505733" />
                                        <XAxis dataKey="dayLabel" stroke="#adb5bd" tick={{ fontSize: 10 }} />
                                        <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} width={52} />
                                        <Tooltip
                                          formatter={(value: number) => formatInrRupee(value)}
                                          labelFormatter={(_, payload) =>
                                            payload?.[0]?.payload?.reportDate != null
                                              ? String(payload[0].payload.reportDate)
                                              : ''
                                          }
                                          contentStyle={{
                                            background: '#212529',
                                            border: '1px solid #495057',
                                            borderRadius: 8,
                                            fontSize: 12,
                                          }}
                                        />
                                        <ReferenceLine y={0} stroke="#6c757d" strokeDasharray="4 4" />
                                        <Line
                                          type="monotone"
                                          dataKey="cumulativeNet"
                                          name="Cumulative net"
                                          stroke="#0dcaf0"
                                          dot={{ r: 3, fill: '#0dcaf0' }}
                                          strokeWidth={2}
                                        />
                                      </LineChart>
                                    </ResponsiveContainer>
                                    </ChartWithRightGutter>
                                  </div>
                                </Col>
                              </Row>
                            ) : null}
                            <div className="table-responsive border border-secondary-subtle rounded mb-3">
                              <Table size="sm" striped bordered hover className="mb-0 small">
                                <thead>
                                  <tr>
                                    <th>Date</th>
                                    <th className="text-end">Signals</th>
                                    <th className="text-end">Gross</th>
                                    <th className="text-end">Fees</th>
                                    <th className="text-end">Net</th>
                                    <th className="text-end">Dir. legs</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {demoFullReport.dailySummaries.map((d) => (
                                    <tr key={d.reportDate}>
                                      <td className="font-monospace">{d.reportDate}</td>
                                      <td className="text-end">{d.totalSignals}</td>
                                      <td className="text-end">{formatInrRupee(d.hypotheticalGrossPnlInr)}</td>
                                      <td className="text-end">{formatInrRupee(d.hypotheticalChargesInr)}</td>
                                      <td
                                        className={`text-end fw-semibold ${
                                          d.hypotheticalTotalPnlInr > 0
                                            ? 'text-success'
                                            : d.hypotheticalTotalPnlInr < 0
                                              ? 'text-danger'
                                              : ''
                                        }`}
                                      >
                                        {formatInrRupee(d.hypotheticalTotalPnlInr)}
                                      </td>
                                      <td className="text-end">{d.directionalTradeableLegs}</td>
                                    </tr>
                                  ))}
                                </tbody>
                              </Table>
                            </div>
                          </>
                        ) : (
                          <p className="text-muted mb-2">No automation rows in this window.</p>
                        )}
                        {demoFullReport.outcomesByEngine.length > 0 ? (
                          <>
                            <div className="fw-semibold text-secondary mb-1">Outcomes by engine</div>
                            <div className="table-responsive border border-secondary-subtle rounded mb-3">
                              <Table size="sm" striped bordered hover className="mb-0 small">
                                <thead>
                                  <tr>
                                    <th>Engine</th>
                                    <th className="text-end">Total</th>
                                    <th className="text-end">Pending</th>
                                    <th className="text-end">Correct</th>
                                    <th className="text-end">Wrong</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {demoFullReport.outcomesByEngine.map((r) => (
                                    <tr key={r.key}>
                                      <td className="font-monospace text-break">{r.key}</td>
                                      <td className="text-end">{r.total}</td>
                                      <td className="text-end">{r.pending}</td>
                                      <td className="text-end">{r.correct}</td>
                                      <td className="text-end">{r.wrong}</td>
                                    </tr>
                                  ))}
                                </tbody>
                              </Table>
                            </div>
                          </>
                        ) : null}
                        {demoFullReport.outcomesByInterval.length > 0 ? (
                          <>
                            <div className="fw-semibold text-secondary mb-1">Outcomes by interval</div>
                            <div className="table-responsive border border-secondary-subtle rounded mb-0">
                              <Table size="sm" striped bordered hover className="mb-0 small">
                                <thead>
                                  <tr>
                                    <th>Interval</th>
                                    <th className="text-end">Total</th>
                                    <th className="text-end">Pending</th>
                                    <th className="text-end">Correct</th>
                                    <th className="text-end">Wrong</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {demoFullReport.outcomesByInterval.map((r) => (
                                    <tr key={r.key}>
                                      <td className="font-monospace">{r.key}</td>
                                      <td className="text-end">{r.total}</td>
                                      <td className="text-end">{r.pending}</td>
                                      <td className="text-end">{r.correct}</td>
                                      <td className="text-end">{r.wrong}</td>
                                    </tr>
                                  ))}
                                </tbody>
                              </Table>
                            </div>
                          </>
                        ) : null}
                      </div>
                    ) : null}
                  </div>
                </div>
              </div>

            </div>
            ) : null}

            {favoritesError ? (
              <Alert variant="danger" className="mt-2 py-2 small mb-0">
                Favorites: {favoritesError}
              </Alert>
            ) : null}

            {tradingLocksError ? (
              <Alert variant="danger" className="mt-2 py-2 small mb-0">
                Trading locks: {tradingLocksError}
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
                    <>
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
                            {todayTopPerformers.slice(0, todayTopVisibleCount).map((row, idx) => {
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
                      {todayTopPerformers.length > TODAY_TOP_MOVERS_PAGE_SIZE ||
                      todayTopVisibleCount < todayTopPerformers.length ? (
                        <p className="small text-secondary mb-2 mt-2">
                          Showing{' '}
                          {Math.min(todayTopVisibleCount, todayTopPerformers.length)} of{' '}
                          {todayTopPerformers.length}
                        </p>
                      ) : null}
                      {todayTopVisibleCount < todayTopPerformers.length ? (
                        <Button
                          type="button"
                          variant="outline-secondary"
                          size="sm"
                          disabled={todayTopLoading}
                          onClick={() =>
                            setTodayTopVisibleCount((c) =>
                              Math.min(c + TODAY_TOP_MOVERS_PAGE_SIZE, todayTopPerformers.length),
                            )
                          }
                        >
                          Load more
                        </Button>
                      ) : null}
                    </>
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
                  tradingLockKeySet={tradingLockKeySet}
                  onToggleTradingLock={(r) => void toggleTradingLock(r)}
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
                  tradingLockKeySet={tradingLockKeySet}
                  onToggleTradingLock={(r) => void toggleTradingLock(r)}
                />
                <InstrumentListPanel
                  title="Spot equity (NSE / BSE cash)"
                  rows={EMPTY_INSTRUMENTS}
                  truncated={false}
                  loading={false}
                  emptyHint="Type a symbol or company name, then press Enter or Search Kite. Only equity cash (EQ segment) matches are returned."
                  searchSegment="spot"
                  selectedRowKey={chartRow ? favoriteRowKey(chartRow) : null}
                  onSelectRow={setChartRow}
                  favoriteKeySet={favoriteKeySet}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                  tradingLockKeySet={tradingLockKeySet}
                  onToggleTradingLock={(r) => void toggleTradingLock(r)}
                />
              </>
            ) : mainTab === 'favorites' ? (
              <div className="mt-3">
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
                  tradingLockKeySet={tradingLockKeySet}
                  onToggleTradingLock={(r) => void toggleTradingLock(r)}
                />
                <FavoritesChartsGrid
                  favorites={favorites}
                  rangePreset={chartRangePreset}
                  onRangePresetChange={setChartRangePreset}
                  defaultInterval={chartInterval}
                  onDefaultIntervalChange={applyIntervalToAllFavoriteCharts}
                  chartIntervalByInstrumentToken={chartIntervalByToken}
                  onInstrumentIntervalChange={persistInstrumentChartInterval}
                  graphType={chartGraphType}
                  onGraphTypeChange={setChartGraphType}
                  maLineVisibility={maLineVisibility}
                  onMaLineVisibilityChange={patchMaLineVisibility}
                  customEmaPeriod={customEmaPeriod}
                  onCustomEmaPeriodChange={setCustomEmaPeriod}
                  trendAnalysisSelections={trendAnalysisSelections}
                  onTrendAnalysisSelectionsChange={setTrendAnalysisSelections}
                  chartZoomByInstrumentToken={chartZoomByToken}
                  onInstrumentChartZoomChange={persistInstrumentChartZoom}
                  listTilePrimaryAction={(r) => void toggleFavorite(r)}
                  listTilePrimaryLabel="★ Remove"
                  tradingLockKeySet={tradingLockKeySet}
                  onToggleTradingLock={(r) => void toggleTradingLock(r)}
                  automationRecent={automationRecent}
                  automationRecentLoading={automationRecentLoading}
                  automationPriceModels={automationPriceModels}
                  zerodhaConnected={isZerodha}
                  demoPaperOpenBuysByInstrumentToken={demoPaperOpenBuysByInstrumentToken}
                  demoPaperLastBuyPriceByInstrumentToken={demoPaperLastBuyPriceByInstrumentToken}
                />
              </div>
            ) : mainTab === 'tradingLocks' ? (
              <div className="mt-3">
                <InstrumentListPanel
                  title="Locked for trading"
                  rows={tradingLocks}
                  truncated={false}
                  loading={false}
                  emptyHint="No instruments locked yet. On Browse (or All favorites), use 🔓 to lock a row for trading."
                  searchSegment="fno"
                  kiteLiveSegmentScope="all"
                  selectedRowKey={chartRow ? favoriteRowKey(chartRow) : null}
                  onSelectRow={setChartRow}
                  favoriteKeySet={favoriteKeySet}
                  onToggleFavorite={(r) => void toggleFavorite(r)}
                  tradingLockKeySet={tradingLockKeySet}
                  onToggleTradingLock={(r) => void toggleTradingLock(r)}
                />
                <FavoritesChartsGrid
                  favorites={tradingLocks}
                  rangePreset={chartRangePreset}
                  onRangePresetChange={setChartRangePreset}
                  defaultInterval={chartInterval}
                  onDefaultIntervalChange={applyIntervalToAllFavoriteCharts}
                  chartIntervalByInstrumentToken={chartIntervalByToken}
                  onInstrumentIntervalChange={persistInstrumentChartInterval}
                  graphType={chartGraphType}
                  onGraphTypeChange={setChartGraphType}
                  maLineVisibility={maLineVisibility}
                  onMaLineVisibilityChange={patchMaLineVisibility}
                  customEmaPeriod={customEmaPeriod}
                  onCustomEmaPeriodChange={setCustomEmaPeriod}
                  trendAnalysisSelections={trendAnalysisSelections}
                  onTrendAnalysisSelectionsChange={setTrendAnalysisSelections}
                  chartZoomByInstrumentToken={chartZoomByToken}
                  onInstrumentChartZoomChange={persistInstrumentChartZoom}
                  listTilePrimaryAction={(r) => void toggleTradingLock(r)}
                  listTilePrimaryLabel="🔒 Remove lock"
                  automationRecent={automationRecent}
                  automationRecentLoading={automationRecentLoading}
                  automationPriceModels={automationPriceModels}
                  zerodhaConnected={isZerodha}
                  demoPaperOpenBuysByInstrumentToken={demoPaperOpenBuysByInstrumentToken}
                  demoPaperLastBuyPriceByInstrumentToken={demoPaperLastBuyPriceByInstrumentToken}
                />
              </div>
            ) : null}

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
                isTradingLocked={chartRow ? tradingLockKeySet.has(favoriteRowKey(chartRow)) : false}
                onToggleTradingLock={
                  chartRow ? () => void toggleTradingLock(chartRow) : undefined
                }
                liveLastPrice={liveLtp}
                liveLastTick={liveLastTick}
                chartZoomStored={
                  chartRow ? chartZoomByToken[chartRow.instrumentToken] ?? null : null
                }
                onChartZoomStoredChange={(stored) => {
                  if (chartRow) persistInstrumentChartZoom(chartRow.instrumentToken, stored)
                }}
                trendAnalysisSelections={trendAnalysisSelections}
                onTrendAnalysisSelectionsChange={setTrendAnalysisSelections}
                demoPaperBuyMarkers={browseDemoPaperOpenBuys}
                paperLastBuyPrice={browseDemoPaperLastBuyPrice}
              />
            ) : null}
          </Card.Body>
        </Card>
      ) : null}
    </Layout>
  )
}
