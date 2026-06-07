import axios from 'axios'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Alert,
  Badge,
  Button,
  ButtonGroup,
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
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import { InstrumentPriceChart } from '../components/InstrumentPriceChart'
import { Layout } from '../components/Layout'
import { TrendAnalysisMultiPanel } from '../components/TrendAnalysisMultiPanel'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import { useChartOlderBars } from '../hooks/useChartOlderBars'
import { type ChartGraphType } from '../utils/kiteInstrumentChartShared'
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
import type { LiveTickVolumeAccumulator } from '../utils/liveCandleMerge'
import {
  addCustomEmaToChartPoints,
  CUSTOM_EMA_DEFAULT_PERIOD,
  type ChartPointWithMa,
  type MaLineVisibility,
} from '../utils/movingAverages'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
import { isIstMarketLiveWindow } from '../utils/marketHours'

const SCALPER_TREND_INTERVAL_OPTIONS: ReadonlyArray<{ id: string; label: string }> = [
  { id: '1m', label: '1m' },
  { id: '2m', label: '2m' },
  { id: '3m', label: '3m' },
  { id: '5m', label: '5m' },
  { id: '10m', label: '10m' },
  { id: '15m', label: '15m' },
  { id: '30m', label: '30m' },
  { id: '1h', label: '1h' },
  { id: '90m', label: '1.5h' },
  { id: '2h', label: '2h' },
  { id: '4h', label: '4h' },
  { id: '8h', label: '8h' },
]
const SCALPER_ATM_POLL_MS = 5_000

interface KiteInstrumentLiveQuoteResponse {
  exchange: string
  tradingsymbol: string
  lastPrice: number
  previousClose: number
}

interface ScalperAtmTarget {
  key: string
  label: string
  spotQuery: string
  optionQuery: string
  preferredSpotExchange?: string
}

const SCALPER_ATM_TARGETS: ScalperAtmTarget[] = [
  { key: 'nifty', label: 'NIFTY', spotQuery: 'NIFTY 50', optionQuery: 'NIFTY', preferredSpotExchange: 'NSE' },
  { key: 'banknifty', label: 'BANKNIFTY', spotQuery: 'NIFTY BANK', optionQuery: 'BANKNIFTY', preferredSpotExchange: 'NSE' },
  { key: 'finnifty', label: 'FINNIFTY', spotQuery: 'NIFTY FIN SERVICE', optionQuery: 'FINNIFTY', preferredSpotExchange: 'NSE' },
  { key: 'midcpnifty', label: 'MIDCPNIFTY', spotQuery: 'NIFTY MID SELECT', optionQuery: 'MIDCPNIFTY', preferredSpotExchange: 'NSE' },
  { key: 'sensex', label: 'SENSEX', spotQuery: 'SENSEX', optionQuery: 'SENSEX', preferredSpotExchange: 'BSE' },
  { key: 'bankex', label: 'BANKEX', spotQuery: 'BANKEX', optionQuery: 'BANKEX', preferredSpotExchange: 'BSE' },
]

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

interface InstrumentSearchResponse {
  items: KiteInstrumentRow[]
  scanTruncated: boolean
}

interface KiteOrderActionResultResponse {
  orderId: string
  action: string
  message: string
}

interface ScalperSettingsResponse {
  interval: ScalperInterval
  rangePreset: ScalperRange
  graphType: ChartGraphType
  showVolume: boolean
  safeModeEnabled: boolean
  safeStopLossPoints: number
  safeTriggerPoints: number
}

type ScalperTicketOrderType = 'MARKET' | 'LIMIT' | 'SL' | 'SL-M'

const SCALPER_TICKET_ORDER_TYPES: ReadonlyArray<ScalperTicketOrderType> = ['MARKET', 'LIMIT', 'SL', 'SL-M']
const SAFE_SCALPER_POINT_PRESETS: ReadonlyArray<{ stop: string; trigger: string; label: string }> = [
  { stop: '10', trigger: '20', label: 'N10 / M20' },
  { stop: '15', trigger: '30', label: 'N15 / M30' },
  { stop: '20', trigger: '40', label: 'N20 / M40' },
  { stop: '25', trigger: '50', label: 'N25 / M50' },
]

type ChartContextPriceMenuState = { x: number; y: number; price: number } | null

type ScalperAtmChainRow = {
  strike: number
  isAtm: boolean
  ceRow: KiteInstrumentRow | null
  peRow: KiteInstrumentRow | null
  ceQuote: KiteInstrumentLiveQuoteResponse | null
  peQuote: KiteInstrumentLiveQuoteResponse | null
}

type ScalperAtmSnapshot = {
  spotQuote: KiteInstrumentLiveQuoteResponse | null
  optionExpiry: string | null
  atmStrike: number | null
  ceRow: KiteInstrumentRow | null
  peRow: KiteInstrumentRow | null
  ceQuote: KiteInstrumentLiveQuoteResponse | null
  peQuote: KiteInstrumentLiveQuoteResponse | null
  chainRows: ScalperAtmChainRow[]
}

const EMPTY_ATM_SNAPSHOT: ScalperAtmSnapshot = {
  spotQuote: null,
  optionExpiry: null,
  atmStrike: null,
  ceRow: null,
  peRow: null,
  ceQuote: null,
  peQuote: null,
  chainRows: [],
}

const SCALPER_ATM_CACHE_PREFIX = 'scalper:atm:last:'

function scalperAtmCacheKey(targetKey: string): string {
  return `${SCALPER_ATM_CACHE_PREFIX}${targetKey}`
}

function loadScalperAtmFromCache(targetKey: string): { snapshot: ScalperAtmSnapshot; updatedAt: string | null } | null {
  try {
    const raw = window.localStorage.getItem(scalperAtmCacheKey(targetKey))
    if (!raw) return null
    const parsed = JSON.parse(raw) as { snapshot?: ScalperAtmSnapshot; updatedAt?: string | null }
    if (!parsed?.snapshot) return null
    return { snapshot: parsed.snapshot, updatedAt: parsed.updatedAt ?? null }
  } catch {
    return null
  }
}

function saveScalperAtmToCache(targetKey: string, snapshot: ScalperAtmSnapshot, updatedAt: string | null): void {
  try {
    window.localStorage.setItem(scalperAtmCacheKey(targetKey), JSON.stringify({ snapshot, updatedAt }))
  } catch {
    /* ignore storage failures */
  }
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

function effectiveCustomEmaPeriod(visibility: MaLineVisibility): number | null {
  if (!visibility.showCustomEma) return null
  return CUSTOM_EMA_DEFAULT_PERIOD
}

function parseExpiryIsoToMs(expiryIso: string | null): number | null {
  if (!expiryIso) return null
  const ms = Date.parse(expiryIso)
  return Number.isFinite(ms) ? ms : null
}

function isOptionRow(row: KiteInstrumentRow): boolean {
  const kind = (row.instrumentType ?? '').trim().toUpperCase()
  if (kind === 'CE' || kind === 'PE') return true
  const ts = row.tradingsymbol.trim().toUpperCase()
  return ts.endsWith('CE') || ts.endsWith('PE')
}

function chooseAtmSpotRow(rows: KiteInstrumentRow[], target: ScalperAtmTarget): KiteInstrumentRow | null {
  const norm = (s: string | null | undefined) => (s ?? '').replace(/\s+/g, '').toUpperCase()
  const targetNorm = norm(target.label)
  const exact = rows.find((r) => norm(r.tradingsymbol) === targetNorm)
  if (exact) return exact
  const exactName = rows.find((r) => norm(r.name) === targetNorm)
  if (exactName) return exactName
  const preferredExchange = (target.preferredSpotExchange ?? '').trim().toUpperCase()
  const fallback =
    preferredExchange.length > 0
      ? rows.find((r) => r.exchange.trim().toUpperCase() === preferredExchange)
      : null
  return fallback ?? rows[0] ?? null
}

function filterOptionsForUnderlying(rows: KiteInstrumentRow[], target: ScalperAtmTarget): KiteInstrumentRow[] {
  const needle = target.label.replace(/\s+/g, '').toUpperCase()
  return rows.filter((r) => {
    if (!isOptionRow(r) || r.strike == null || parseExpiryIsoToMs(r.expiry) == null) return false
    const ts = r.tradingsymbol.trim().toUpperCase().replace(/\s+/g, '')
    const nm = (r.name ?? '').trim().toUpperCase().replace(/\s+/g, '')
    return ts.includes(needle) || nm.includes(needle)
  })
}

function pickNearestFutureExpiry(options: KiteInstrumentRow[]): string | null {
  const now = Date.now()
  const futureMs = options
    .map((r) => parseExpiryIsoToMs(r.expiry))
    .filter((x): x is number => x != null && x >= now)
    .sort((a, b) => a - b)
  if (futureMs.length > 0) return new Date(futureMs[0]!).toISOString()
  const anyMs = options
    .map((r) => parseExpiryIsoToMs(r.expiry))
    .filter((x): x is number => x != null)
    .sort((a, b) => a - b)
  return anyMs.length > 0 ? new Date(anyMs[0]!).toISOString() : null
}

function pickAtmLegs(
  options: KiteInstrumentRow[],
  spotLtp: number,
): { expiry: string | null; strike: number | null; ce: KiteInstrumentRow | null; pe: KiteInstrumentRow | null } {
  if (!Number.isFinite(spotLtp) || options.length === 0) return { expiry: null, strike: null, ce: null, pe: null }
  const expiryIso = pickNearestFutureExpiry(options)
  if (!expiryIso) return { expiry: null, strike: null, ce: null, pe: null }
  const expiryMs = Date.parse(expiryIso)
  const bucket = options.filter((r) => parseExpiryIsoToMs(r.expiry) === expiryMs && r.strike != null)
  if (bucket.length === 0) return { expiry: expiryIso, strike: null, ce: null, pe: null }
  const strikes = [...new Set(bucket.map((r) => Number(r.strike)).filter(Number.isFinite))]
  if (strikes.length === 0) return { expiry: expiryIso, strike: null, ce: null, pe: null }
  const strike = strikes.reduce((best, s) => (Math.abs(s - spotLtp) < Math.abs(best - spotLtp) ? s : best), strikes[0]!)
  const strikeRows = bucket.filter((r) => Number(r.strike) === strike)
  const ce =
    strikeRows.find((r) => (r.instrumentType ?? '').toUpperCase() === 'CE' || r.tradingsymbol.toUpperCase().endsWith('CE')) ??
    null
  const pe =
    strikeRows.find((r) => (r.instrumentType ?? '').toUpperCase() === 'PE' || r.tradingsymbol.toUpperCase().endsWith('PE')) ??
    null
  return { expiry: expiryIso, strike, ce, pe }
}

function buildAtmChainRows(
  options: KiteInstrumentRow[],
  expiryIso: string | null,
  atmStrike: number | null,
  strikesEachSide: number,
): Array<{ strike: number; isAtm: boolean; ceRow: KiteInstrumentRow | null; peRow: KiteInstrumentRow | null }> {
  if (!expiryIso || atmStrike == null) return []
  const expiryMs = Date.parse(expiryIso)
  if (!Number.isFinite(expiryMs)) return []
  const bucket = options.filter((r) => parseExpiryIsoToMs(r.expiry) === expiryMs && r.strike != null)
  const strikes = [...new Set(bucket.map((r) => Number(r.strike)).filter(Number.isFinite))].sort((a, b) => a - b)
  if (strikes.length === 0) return []
  const atmIdx = strikes.reduce(
    (bestIdx, s, i) => (Math.abs(s - atmStrike) < Math.abs(strikes[bestIdx]! - atmStrike) ? i : bestIdx),
    0,
  )
  const from = Math.max(0, atmIdx - strikesEachSide)
  const to = Math.min(strikes.length - 1, atmIdx + strikesEachSide)
  return strikes.slice(from, to + 1).map((strike) => {
    const strikeRows = bucket.filter((r) => Number(r.strike) === strike)
    const ceRow =
      strikeRows.find((r) => (r.instrumentType ?? '').toUpperCase() === 'CE' || r.tradingsymbol.toUpperCase().endsWith('CE')) ??
      null
    const peRow =
      strikeRows.find((r) => (r.instrumentType ?? '').toUpperCase() === 'PE' || r.tradingsymbol.toUpperCase().endsWith('PE')) ??
      null
    return { strike, isAtm: strike === strikes[atmIdx], ceRow, peRow }
  })
}

export function ScalperPage() {
  const [isSafeMode, setIsSafeMode] = useState(false)
  const [broker, setBroker] = useState<BrokerStatusResponse | null>(null)

  const [selected, setSelected] = useState<KiteInstrumentRow | null>(null)
  const [interval, setInterval] = useState<ScalperInterval>('1m')
  const [rangePreset, setRangePreset] = useState<ScalperRange>('last3d')
  const [showVolume, setShowVolume] = useState(true)
  const [graphType, setGraphType] = useState<ChartGraphType>('candlestick')
  const [maLineVisibility, setMaLineVisibility] = useState<MaLineVisibility>(SCALPER_MA)

  const [rawSeries, setRawSeries] = useState<ChartPointWithMa[]>([])
  const rawSeriesRef = useRef<ChartPointWithMa[] | null>(null)
  const liveVolAccRef = useRef<LiveTickVolumeAccumulator>({ lastCumulativeVolume: null })
  const [candleMeta, setCandleMeta] = useState<{ interval: string; from: string; to: string } | null>(null)
  const [chartError, setChartError] = useState<string | null>(null)
  const [chartLoading, setChartLoading] = useState(false)

  const [searchQ, setSearchQ] = useState('')
  const [searchItems, setSearchItems] = useState<KiteInstrumentRow[]>([])
  const [searchBusy, setSearchBusy] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const [atmTargetKey, setAtmTargetKey] = useState<string>(SCALPER_ATM_TARGETS[0]!.key)
  const [atmReferenceLoading, setAtmReferenceLoading] = useState(false)
  const [atmLiveLoading, setAtmLiveLoading] = useState(false)
  const [atmError, setAtmError] = useState<string | null>(null)
  const [atmLastUpdatedAt, setAtmLastUpdatedAt] = useState<string | null>(null)
  const [atmSpotRow, setAtmSpotRow] = useState<KiteInstrumentRow | null>(null)
  const [atmOptionRows, setAtmOptionRows] = useState<KiteInstrumentRow[]>([])
  const [atmSnapshot, setAtmSnapshot] = useState<ScalperAtmSnapshot>(EMPTY_ATM_SNAPSHOT)
  const [isLivePullWindow, setIsLivePullWindow] = useState<boolean>(() => isIstMarketLiveWindow())
  const [tradeQty, setTradeQty] = useState('1')
  const [tradeProduct, setTradeProduct] = useState<'MIS' | 'NRML'>('MIS')
  const [tradeOrderType, setTradeOrderType] = useState<ScalperTicketOrderType>('MARKET')
  const [tradePrice, setTradePrice] = useState('')
  const [tradeTriggerPrice, setTradeTriggerPrice] = useState('')
  const [tradeStopLossPrice, setTradeStopLossPrice] = useState('')
  const [tradeBusy, setTradeBusy] = useState(false)
  const [tradeInfo, setTradeInfo] = useState<string | null>(null)
  const [tradeError, setTradeError] = useState<string | null>(null)
  const [lastEntrySide, setLastEntrySide] = useState<'BUY' | 'SELL' | null>(null)
  const [safeStopLossPoints, setSafeStopLossPoints] = useState('10')
  const [safeSellTriggerPoints, setSafeSellTriggerPoints] = useState('20')
  const [scalperPrefsHydrated, setScalperPrefsHydrated] = useState(false)
  const [activeTradeSide, setActiveTradeSide] = useState<'BUY' | 'SELL' | null>(null)
  const [entryLinePrice, setEntryLinePrice] = useState<number | null>(null)
  const [triggerLinePrice, setTriggerLinePrice] = useState<number | null>(null)
  const [stopLinePrice, setStopLinePrice] = useState<number | null>(null)
  const [chartContextPriceMenu, setChartContextPriceMenu] = useState<ChartContextPriceMenuState>(null)

  const isZerodha = broker?.connected === true && (broker?.provider ?? '').toLowerCase() === 'zerodha'
  const atmTarget = useMemo(
    () => SCALPER_ATM_TARGETS.find((t) => t.key === atmTargetKey) ?? SCALPER_ATM_TARGETS[0]!,
    [atmTargetKey],
  )

  const live = useLiveMarketTick(
    selected?.instrumentToken ?? null,
    isZerodha && selected != null && isLivePullWindow,
  )

  const parsePositiveNumber = (raw: string): number | null => {
    const n = Number(raw)
    if (!Number.isFinite(n) || n <= 0) return null
    return n
  }

  useEffect(() => {
    if (!selected) return
    const lot = selected.lotSize ?? 1
    setTradeQty(String(Math.max(1, Math.floor(lot))))
    setTradePrice('')
    setTradeTriggerPrice('')
    setTradeStopLossPrice('')
    setTradeInfo(null)
    setTradeError(null)
    setLastEntrySide(null)
    setActiveTradeSide(null)
    setEntryLinePrice(null)
    setTriggerLinePrice(null)
    setStopLinePrice(null)
  }, [selected?.instrumentToken, selected?.lotSize])

  useEffect(() => {
    let dead = false
    ;(async () => {
      try {
        const { data } = await api.get<ScalperSettingsResponse>('/broker/kite/scalper-settings')
        if (dead) return
        if (SCALPER_INTERVALS.includes(data.interval)) setInterval(data.interval)
        if (SCALPER_RANGES.some((x) => x.id === data.rangePreset)) setRangePreset(data.rangePreset)
        if (data.graphType === 'line' || data.graphType === 'bar' || data.graphType === 'candlestick')
          setGraphType(data.graphType)
        setShowVolume(Boolean(data.showVolume))
        setIsSafeMode(Boolean(data.safeModeEnabled))
        setSafeStopLossPoints(
          typeof data.safeStopLossPoints === 'number' && Number.isFinite(data.safeStopLossPoints) && data.safeStopLossPoints > 0
            ? String(data.safeStopLossPoints)
            : '10',
        )
        setSafeSellTriggerPoints(
          typeof data.safeTriggerPoints === 'number' && Number.isFinite(data.safeTriggerPoints) && data.safeTriggerPoints > 0
            ? String(data.safeTriggerPoints)
            : '20',
        )
      } catch {
        /* keep defaults */
      } finally {
        if (!dead) setScalperPrefsHydrated(true)
      }
    })()
    return () => {
      dead = true
    }
  }, [])

  useEffect(() => {
    if (!scalperPrefsHydrated) return
    const stopPts = Number(safeStopLossPoints)
    const triggerPts = Number(safeSellTriggerPoints)
    const tid = window.setTimeout(() => {
      void api.put('/broker/kite/scalper-settings', {
        interval,
        rangePreset,
        graphType,
        showVolume,
        safeModeEnabled: isSafeMode,
        safeStopLossPoints:
          Number.isFinite(stopPts) && stopPts > 0 ? stopPts : 10,
        safeTriggerPoints:
          Number.isFinite(triggerPts) && triggerPts > 0 ? triggerPts : 20,
      }).catch(() => {
        /* non-fatal */
      })
    }, 450)
    return () => window.clearTimeout(tid)
  }, [
    graphType,
    interval,
    isSafeMode,
    rangePreset,
    safeSellTriggerPoints,
    safeStopLossPoints,
    scalperPrefsHydrated,
    showVolume,
  ])

  const onTradeTriggerInputChange = useCallback((raw: string) => {
    setTradeTriggerPrice(raw)
    const n = Number(raw)
    setTriggerLinePrice(Number.isFinite(n) && n > 0 ? Number(n.toFixed(2)) : null)
  }, [])

  const onTradeStopInputChange = useCallback((raw: string) => {
    setTradeStopLossPrice(raw)
    const n = Number(raw)
    setStopLinePrice(Number.isFinite(n) && n > 0 ? Number(n.toFixed(2)) : null)
  }, [])

  const placeScalperOrder = useCallback(
    async (
      intent: 'BUY' | 'SELL' | 'EXIT',
      overrides?: {
        orderType?: ScalperTicketOrderType
        price?: number | null
        triggerPrice?: number | null
      },
    ) => {
      if (!selected || !isZerodha) return
      const qtyN = Number(tradeQty)
      const quantity = Number.isFinite(qtyN) ? Math.max(1, Math.floor(qtyN)) : 0
      if (quantity < 1) {
        setTradeError('Quantity must be at least 1.')
        return
      }

      const transactionType: 'BUY' | 'SELL' =
        intent === 'EXIT'
          ? lastEntrySide === 'BUY'
            ? 'SELL'
            : lastEntrySide === 'SELL'
              ? 'BUY'
              : 'SELL'
          : intent

      const effectiveOrderType = overrides?.orderType ?? tradeOrderType
      const price = overrides?.price ?? parsePositiveNumber(tradePrice)
      const trigger = overrides?.triggerPrice ?? parsePositiveNumber(tradeTriggerPrice)
      if (effectiveOrderType === 'LIMIT' && price == null) {
        setTradeError('LIMIT order requires a valid price.')
        return
      }
      if (effectiveOrderType === 'SL' && (price == null || trigger == null)) {
        setTradeError('SL order requires both price and trigger.')
        return
      }
      if (effectiveOrderType === 'SL-M' && trigger == null) {
        setTradeError('SL-M order requires trigger price.')
        return
      }

      const normalizedPrice =
        effectiveOrderType === 'LIMIT' || effectiveOrderType === 'SL'
          ? price
          : null
      const normalizedTrigger =
        effectiveOrderType === 'SL' || effectiveOrderType === 'SL-M'
          ? trigger
          : null

      setTradeBusy(true)
      setTradeError(null)
      setTradeInfo(null)
      try {
        const placeRes = await api.post<KiteOrderActionResultResponse>('/broker/kite/orders/place', {
          variety: 'regular',
          exchange: selected.exchange,
          tradingsymbol: selected.tradingsymbol,
          transactionType,
          quantity,
          product: tradeProduct,
          orderType: effectiveOrderType,
          validity: 'DAY',
          price: normalizedPrice,
          triggerPrice: normalizedTrigger,
          tag: 'scalper',
        })

        const notes: string[] = [
          `${intent} placed (${transactionType}) · order ${placeRes.data.orderId}`,
        ]

        const executionLikePrice = normalizedPrice ?? (live.lastPrice != null ? Number(live.lastPrice.toFixed(2)) : null)
        if (isSafeMode && intent !== 'EXIT' && (executionLikePrice == null || !Number.isFinite(executionLikePrice))) {
          throw new Error('Safe Scalper needs a valid execution price (LTP/limit) to compute auto trigger and stop-loss.')
        }

        const safeStopPts = parsePositiveNumber(safeStopLossPoints)
        const safeTriggerPts = parsePositiveNumber(safeSellTriggerPoints)
        if (isSafeMode && intent !== 'EXIT' && (safeStopPts == null || safeTriggerPts == null)) {
          throw new Error('Safe Scalper requires valid N/M point values for auto stop-loss and trigger.')
        }
        const autoFromSafe =
          isSafeMode &&
          intent !== 'EXIT' &&
          executionLikePrice != null &&
          Number.isFinite(executionLikePrice) &&
          safeStopPts != null &&
          safeTriggerPts != null

        const computedStopLoss =
          autoFromSafe
            ? transactionType === 'BUY'
              ? Number((executionLikePrice - safeStopPts).toFixed(2))
              : Number((executionLikePrice + safeStopPts).toFixed(2))
            : null
        const computedTrigger =
          autoFromSafe
            ? transactionType === 'BUY'
              ? Number((executionLikePrice + safeTriggerPts).toFixed(2))
              : Number((executionLikePrice - safeTriggerPts).toFixed(2))
            : null

        const stopLoss = computedStopLoss ?? parsePositiveNumber(tradeStopLossPrice)
        if (intent !== 'EXIT' && stopLoss != null) {
          const protectiveSide = transactionType === 'BUY' ? 'SELL' : 'BUY'
          const slRes = await api.post<KiteOrderActionResultResponse>('/broker/kite/orders/place', {
            variety: 'regular',
            exchange: selected.exchange,
            tradingsymbol: selected.tradingsymbol,
            transactionType: protectiveSide,
            quantity,
            product: tradeProduct,
            orderType: 'SL-M',
            validity: 'DAY',
            triggerPrice: stopLoss,
            tag: 'scalper-sl',
          })
          notes.push(`SL trigger set (${protectiveSide}) · order ${slRes.data.orderId}`)
        }

        if (computedTrigger != null) {
          notes.push(`Auto trigger ${computedTrigger.toFixed(2)} (${transactionType === 'BUY' ? 'target up' : 'target down'})`)
        }

        if (intent === 'BUY' || intent === 'SELL') setLastEntrySide(intent)
        if ((intent === 'BUY' || intent === 'SELL') && executionLikePrice != null && Number.isFinite(executionLikePrice)) {
          const side = intent
          const manualTrigger = computedTrigger ?? parsePositiveNumber(tradeTriggerPrice)
          const manualStop = computedStopLoss ?? parsePositiveNumber(tradeStopLossPrice)
          setActiveTradeSide(side)
          setEntryLinePrice(executionLikePrice)
          if (side === 'BUY') {
            const nextTrigger = Number((manualTrigger ?? executionLikePrice * 1.003).toFixed(2))
            const nextStop = Number((manualStop ?? executionLikePrice * 0.997).toFixed(2))
            setTriggerLinePrice(nextTrigger)
            setStopLinePrice(nextStop)
            setTradeTriggerPrice(nextTrigger.toFixed(2))
            setTradeStopLossPrice(nextStop.toFixed(2))
          } else {
            const nextTrigger = Number((manualTrigger ?? executionLikePrice * 0.997).toFixed(2))
            const nextStop = Number((manualStop ?? executionLikePrice * 1.003).toFixed(2))
            setTriggerLinePrice(nextTrigger)
            setStopLinePrice(nextStop)
            setTradeTriggerPrice(nextTrigger.toFixed(2))
            setTradeStopLossPrice(nextStop.toFixed(2))
          }
        }
        if (intent === 'EXIT') {
          setLastEntrySide(null)
          setActiveTradeSide(null)
          setEntryLinePrice(null)
          setTriggerLinePrice(null)
          setStopLinePrice(null)
          setTradeTriggerPrice('')
          setTradeStopLossPrice('')
        }
        setTradeInfo(notes.join(' | '))
      } catch (err) {
        setTradeError(problemDetail(err))
      } finally {
        setTradeBusy(false)
      }
    },
    [
      isZerodha,
      isSafeMode,
      lastEntrySide,
      safeSellTriggerPoints,
      safeStopLossPoints,
      selected,
      tradeOrderType,
      tradePrice,
      tradeProduct,
      tradeQty,
      tradeStopLossPrice,
      tradeTriggerPrice,
      live.lastPrice,
    ],
  )

  useEffect(() => {
    if (!chartContextPriceMenu) return
    const closeMenu = () => setChartContextPriceMenu(null)
    window.addEventListener('click', closeMenu)
    window.addEventListener('scroll', closeMenu, true)
    return () => {
      window.removeEventListener('click', closeMenu)
      window.removeEventListener('scroll', closeMenu, true)
    }
  }, [chartContextPriceMenu])

  const onChartBuyAtPrice = useCallback(
    (price: number) => {
      if (!Number.isFinite(price) || price <= 0) return
      const p = Number(price.toFixed(2))
      setTradeOrderType('LIMIT')
      setTradePrice(p.toFixed(2))
      setTradeTriggerPrice('')
      setChartContextPriceMenu(null)
      void placeScalperOrder('BUY', { orderType: 'LIMIT', price: p, triggerPrice: null })
    },
    [placeScalperOrder],
  )

  const onChartTriggerLineChange = useCallback((price: number) => {
    setTriggerLinePrice(price)
    setTradeTriggerPrice(price.toFixed(2))
  }, [])

  const onChartStopLineChange = useCallback((price: number) => {
    setStopLinePrice(price)
    setTradeStopLossPrice(price.toFixed(2))
  }, [])

  useEffect(() => {
    const refresh = () => setIsLivePullWindow(isIstMarketLiveWindow())
    refresh()
    const id = window.setInterval(refresh, 30_000)
    return () => window.clearInterval(id)
  }, [])

  const loadAtmReferences = useCallback(async () => {
    if (!isZerodha) {
      setAtmSpotRow(null)
      setAtmOptionRows([])
      setAtmError(null)
      return
    }
    setAtmReferenceLoading(true)
    setAtmError(null)
    try {
      const [spotRes, optRes] = await Promise.all([
        api.get<InstrumentSearchResponse>('/broker/kite/instruments/search', {
          params: { q: atmTarget.spotQuery, segment: 'spot' },
        }),
        api.get<InstrumentSearchResponse>('/broker/kite/instruments/search', {
          params: { q: atmTarget.optionQuery, segment: 'fno' },
        }),
      ])
      setAtmSpotRow(chooseAtmSpotRow(spotRes.data.items ?? [], atmTarget))
      setAtmOptionRows(filterOptionsForUnderlying(optRes.data.items ?? [], atmTarget))
    } catch (err) {
      setAtmError(problemDetail(err))
    } finally {
      setAtmReferenceLoading(false)
    }
  }, [atmTarget, isZerodha])

  const refreshAtmLive = useCallback(async () => {
    if (!isZerodha || !atmSpotRow || !isLivePullWindow) return
    setAtmLiveLoading(true)
    setAtmError(null)
    try {
      const fetchQuote = async (row: KiteInstrumentRow | null): Promise<KiteInstrumentLiveQuoteResponse | null> => {
        if (!row) return null
        try {
          const { data } = await api.get<KiteInstrumentLiveQuoteResponse>('/broker/kite/chart/live-quote', {
            params: { exchange: row.exchange, tradingsymbol: row.tradingsymbol },
          })
          return data
        } catch {
          return null
        }
      }

      const previous = atmSnapshot
      const spotQuote = await fetchQuote(atmSpotRow)
      const atm = pickAtmLegs(atmOptionRows, spotQuote?.lastPrice ?? previous.spotQuote?.lastPrice ?? Number.NaN)
      const chainRowsSeed = buildAtmChainRows(atmOptionRows, atm.expiry, atm.strike, 3)

      const uniqueRows = new Map<string, KiteInstrumentRow>()
      for (const row of chainRowsSeed) {
        if (row.ceRow) uniqueRows.set(`ce:${row.ceRow.instrumentToken}`, row.ceRow)
        if (row.peRow) uniqueRows.set(`pe:${row.peRow.instrumentToken}`, row.peRow)
      }
      if (atm.ce) uniqueRows.set(`ce:${atm.ce.instrumentToken}`, atm.ce)
      if (atm.pe) uniqueRows.set(`pe:${atm.pe.instrumentToken}`, atm.pe)

      const quoteEntries = await Promise.all(
        [...uniqueRows.values()].map(async (r) => ({ token: r.instrumentToken, quote: await fetchQuote(r) })),
      )
      const quoteByToken = new Map<string, KiteInstrumentLiveQuoteResponse | null>()
      for (const q of quoteEntries) quoteByToken.set(q.token, q.quote)

      const prevByStrike = new Map(previous.chainRows.map((r) => [r.strike, r]))
      const chainRows = chainRowsSeed.map((row) => {
        const prevRow = prevByStrike.get(row.strike) ?? null
        return {
          strike: row.strike,
          isAtm: row.isAtm,
          ceRow: row.ceRow,
          peRow: row.peRow,
          ceQuote:
            row.ceRow && quoteByToken.get(row.ceRow.instrumentToken) != null
              ? quoteByToken.get(row.ceRow.instrumentToken) ?? null
              : prevRow?.ceQuote ?? null,
          peQuote:
            row.peRow && quoteByToken.get(row.peRow.instrumentToken) != null
              ? quoteByToken.get(row.peRow.instrumentToken) ?? null
              : prevRow?.peQuote ?? null,
        }
      })

      const nextSnapshot: ScalperAtmSnapshot = {
        spotQuote: spotQuote ?? previous.spotQuote ?? null,
        optionExpiry: atm.expiry ?? previous.optionExpiry ?? null,
        atmStrike: atm.strike ?? previous.atmStrike ?? null,
        ceRow: atm.ce ?? previous.ceRow ?? null,
        peRow: atm.pe ?? previous.peRow ?? null,
        ceQuote:
          atm.ce && quoteByToken.get(atm.ce.instrumentToken) != null
            ? quoteByToken.get(atm.ce.instrumentToken) ?? null
            : previous.ceQuote ?? null,
        peQuote:
          atm.pe && quoteByToken.get(atm.pe.instrumentToken) != null
            ? quoteByToken.get(atm.pe.instrumentToken) ?? null
            : previous.peQuote ?? null,
        chainRows: chainRows.length > 0 ? chainRows : previous.chainRows,
      }
      const updatedAt = new Date().toISOString()
      setAtmSnapshot(nextSnapshot)
      setAtmLastUpdatedAt(updatedAt)
      saveScalperAtmToCache(atmTarget.key, nextSnapshot, updatedAt)
    } catch (err) {
      setAtmError(problemDetail(err))
    } finally {
      setAtmLiveLoading(false)
    }
  }, [atmOptionRows, atmSnapshot, atmSpotRow, atmTarget.key, isLivePullWindow, isZerodha])

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
    if (!isZerodha) return
    void loadAtmReferences()
  }, [isZerodha, loadAtmReferences])

  useEffect(() => {
    const cached = loadScalperAtmFromCache(atmTarget.key)
    if (cached) {
      setAtmSnapshot(cached.snapshot)
      setAtmLastUpdatedAt(cached.updatedAt)
      return
    }
    setAtmSnapshot(EMPTY_ATM_SNAPSHOT)
    setAtmLastUpdatedAt(null)
  }, [atmTarget.key])

  useEffect(() => {
    if (!isZerodha || !atmSpotRow || !isLivePullWindow) return
    void refreshAtmLive()
    const id = window.setInterval(() => {
      if (document.visibilityState === 'visible') void refreshAtmLive()
    }, SCALPER_ATM_POLL_MS)
    return () => window.clearInterval(id)
  }, [atmSpotRow, isZerodha, isLivePullWindow, refreshAtmLive])

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
    if (!isZerodha || !selected || !isLivePullWindow) return
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
  }, [isZerodha, selected, interval, rangePreset, isLivePullWindow])

  const displaySeries = useMemo(() => {
    if (rawSeriesRef.current !== rawSeries) {
      rawSeriesRef.current = rawSeries
      liveVolAccRef.current.lastCumulativeVolume = null
    }
    return mergeScalperLiveIntoSeries(rawSeries, live.lastTick, interval, liveVolAccRef.current)
  }, [rawSeries, live.lastTick, interval])

  const customEmaApplied = useMemo(() => effectiveCustomEmaPeriod(maLineVisibility), [maLineVisibility])

  const displaySeriesWithCustom = useMemo(
    () => addCustomEmaToChartPoints(displaySeries, customEmaApplied),
    [displaySeries, customEmaApplied],
  )

  const trendHistoricalExtra = useMemo(() => scalperRangeQueryParams(rangePreset), [rangePreset])
  const defaultTrendIntervals = useMemo(
    () => ['1m', '2m', '3m', '5m', '10m', '15m', '30m', '1h', '4h'],
    [],
  )

  const liveVsBar = useMemo(() => {
    const last = live.lastPrice
    const ref = rawSeries.length > 0 ? rawSeries[rawSeries.length - 1]?.close : null
    if (ref == null || last == null || !Number.isFinite(ref) || !Number.isFinite(last)) return null
    return pctChange(ref, last)
  }, [live.lastPrice, rawSeries])

  const { loadOlderBars, loadingOlderBars, canLoadOlderBars } = useChartOlderBars({
    instrumentToken: selected?.instrumentToken ?? '',
    interval,
    candleWindow: candleMeta ? { from: candleMeta.from, to: candleMeta.to } : null,
    series: rawSeries,
    chartPointsFromMerged: chartPointsFromHistorical,
    setSeries: setRawSeries,
  })

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
        <div className="d-flex align-items-center gap-2">
          <h1 className="h3 mb-0">Scalper</h1>
          <span
            role="img"
            aria-label="Scalper help"
            title={
              isSafeMode
                ? 'Safe Scalper auto-computes trigger/stop-loss points after entry while keeping all standard scalper tools.'
                : 'Scalper shows 3-day candles with live LTP/ticks, ATM chain shortcuts, and multi-interval trend panels.'
            }
            className="text-secondary"
            style={{ cursor: 'help', fontSize: '0.95rem', userSelect: 'none' }}
          >
            ⓘ
          </span>
        </div>
        <div className="d-flex flex-wrap align-items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant={isSafeMode ? 'success' : 'outline-secondary'}
            onClick={() => setIsSafeMode((v) => !v)}
          >
            {isSafeMode ? 'Safe Scalper: ON' : 'Safe Scalper: OFF'}
          </Button>
          <Link to="/instruments" className="btn btn-sm btn-outline-secondary">
            Kite instruments
          </Link>
        </div>
      </div>

      {!isZerodha ? (
        <Alert variant="warning" className="py-2">
          Connect Zerodha under <Link to="/profile#brokers">Profile → Brokers</Link> to use the scalper view.
        </Alert>
      ) : null}

      <Row className="g-3">
        <Col lg={12} xl={12}>
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
                  <span className="text-muted small">Search and pick a symbol to load the chart.</span>
                )}
              </div>
              <div className="position-relative" style={{ minWidth: '16rem', maxWidth: '22rem', width: '100%' }}>
                <InputGroup size="sm">
                  <Form.Control
                    type="search"
                    placeholder="Search symbol..."
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
                  <div className="position-absolute w-100 mt-1 border rounded bg-body shadow-sm" style={{ zIndex: 20 }}>
                    {searchItems.map((r) => (
                      <button
                        key={`${r.exchange}:${r.instrumentToken}`}
                        type="button"
                        className="btn btn-link text-start text-decoration-none small w-100 py-2 px-2 border-bottom rounded-0"
                        onClick={() => {
                          setSelected(r)
                          setSearchOpen(false)
                          setSearchQ('')
                        }}
                      >
                        <span className="font-monospace">{r.tradingsymbol}</span>
                        <span className="text-muted"> · {r.exchange}</span>
                      </button>
                    ))}
                  </div>
                ) : null}
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
              <Button
                type="button"
                size="sm"
                variant={showVolume ? 'outline-success' : 'outline-secondary'}
                onClick={() => setShowVolume((v) => !v)}
              >
                Vol {showVolume ? 'ON' : 'OFF'}
              </Button>
            </Card.Header>
            <Card.Body className="p-2">
              <Row className="g-2">
                <Col lg={7} xl={8}>
                  {chartError ? <Alert variant="danger" className="py-2 small mb-2">{chartError}</Alert> : null}
                  {candleMeta ? (
                    <div className="small text-muted mb-2 font-monospace">
                      {candleMeta.interval} · {formatLocalDateTime(candleMeta.from)} → {formatLocalDateTime(candleMeta.to)} ·
                      refresh ~{SCALPER_POLL_MS / 1000}s + ticks
                    </div>
                  ) : null}
                  {selected ? (
                    <div className="border rounded border-secondary-subtle p-1 mb-2">
                      <div className="d-flex align-items-center gap-2">
                        <div
                          className="small fw-semibold text-nowrap"
                          style={{ fontSize: '0.72rem', letterSpacing: '0.02em', minWidth: '4.8rem' }}
                        >
                          Indicators
                        </div>
                        <div className="d-flex align-items-center gap-1 flex-nowrap overflow-auto ms-auto scalper-compact-scrollbar">
                          <Button
                            size="sm"
                            variant={graphType === 'candlestick' ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setGraphType('candlestick')}
                          >
                            Candles
                          </Button>
                          <Button
                            size="sm"
                            variant={graphType === 'line' ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setGraphType('line')}
                          >
                            Line
                          </Button>
                          <Button
                            size="sm"
                            variant={graphType === 'bar' ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setGraphType('bar')}
                          >
                            Bar
                          </Button>
                          <Button
                            size="sm"
                            variant={maLineVisibility.showSma20 ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setMaLineVisibility((p) => ({ ...p, showSma20: !p.showSma20 }))}
                          >
                            SMA 20
                          </Button>
                          <Button
                            size="sm"
                            variant={maLineVisibility.showEma9 ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setMaLineVisibility((p) => ({ ...p, showEma9: !p.showEma9 }))}
                          >
                            EMA 9
                          </Button>
                          <Button
                            size="sm"
                            variant={maLineVisibility.showEma21 ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setMaLineVisibility((p) => ({ ...p, showEma21: !p.showEma21 }))}
                          >
                            EMA 21
                          </Button>
                          <Button
                            size="sm"
                            variant={maLineVisibility.showCustomEma ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setMaLineVisibility((p) => ({ ...p, showCustomEma: !p.showCustomEma }))}
                          >
                            Custom EMA
                          </Button>
                          <Button
                            size="sm"
                            variant={maLineVisibility.showSupportResistance ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setMaLineVisibility((p) => ({ ...p, showSupportResistance: !p.showSupportResistance }))}
                          >
                            S/R
                          </Button>
                          <Button
                            size="sm"
                            variant={maLineVisibility.showLinearCloseTrend ? 'secondary' : 'outline-secondary'}
                            style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem', whiteSpace: 'nowrap' }}
                            onClick={() => setMaLineVisibility((p) => ({ ...p, showLinearCloseTrend: !p.showLinearCloseTrend }))}
                          >
                            Trend LR
                          </Button>
                        </div>
                      </div>
                    </div>
                  ) : null}
                  {chartLoading && rawSeries.length === 0 ? (
                    <div className="text-center py-5 text-secondary">
                      <Spinner animation="border" className="me-2" />
                      Loading candles…
                    </div>
                  ) : selected && displaySeriesWithCustom.length > 0 ? (
                    <div style={{ height: 'min(58vh, 520px)', minHeight: '320px', position: 'relative' }}>
                      <InstrumentPriceChart
                        graphType={graphType}
                        data={displaySeriesWithCustom}
                        maLineVisibility={maLineVisibility}
                        customEmaPeriod={customEmaApplied}
                        crosshairMode="normal"
                        tradeGuideLines={{
                          entryPrice: entryLinePrice,
                          triggerPrice: triggerLinePrice,
                          stopLossPrice: stopLinePrice,
                          side: activeTradeSide,
                          dragEnabled: !!entryLinePrice && !tradeBusy,
                          onTriggerPriceChange: onChartTriggerLineChange,
                          onStopLossPriceChange: onChartStopLineChange,
                        }}
                        onPriceContextMenu={({ price, x, y }) => {
                          setChartContextPriceMenu({ price, x, y })
                        }}
                        livePrice={live.lastPrice}
                        showVolume={showVolume}
                        newerGhostBars={0}
                        onNeedOlderBars={loadOlderBars}
                        canLoadOlderBars={canLoadOlderBars}
                        loadingOlderBars={loadingOlderBars}
                      />
                      {chartContextPriceMenu ? (
                        <div
                          className="border border-secondary rounded bg-body shadow-sm p-1"
                          style={{
                            position: 'absolute',
                            left: `${Math.max(8, chartContextPriceMenu.x - 8)}px`,
                            top: `${Math.max(8, chartContextPriceMenu.y - 8)}px`,
                            zIndex: 40,
                            minWidth: '11.5rem',
                          }}
                          onClick={(e) => e.stopPropagation()}
                        >
                          <Button
                            size="sm"
                            variant="outline-success"
                            className="w-100 text-start"
                            disabled={tradeBusy}
                            onClick={() => onChartBuyAtPrice(chartContextPriceMenu.price)}
                          >
                            {tradeBusy ? 'Placing…' : `Buy @ ${chartContextPriceMenu.price.toFixed(2)}`}
                          </Button>
                        </div>
                      ) : null}
                    </div>
                  ) : selected && !chartLoading ? (
                    <p className="text-muted small mb-0">No candle data returned for this range.</p>
                  ) : !selected ? (
                    <p className="text-muted small mb-0">Choose an instrument to load the chart.</p>
                  ) : null}
                </Col>
                <Col lg={5} xl={4}>
                  {isZerodha ? (
                    <div className="border rounded border-secondary-subtle p-2 bg-body text-body mb-2">
                      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
                        <div className="d-flex flex-wrap align-items-center gap-2">
                          <div className="small fw-semibold">Live ATM chain</div>
                          {!isLivePullWindow ? (
                            <span
                              role="img"
                              aria-label="Live ATM schedule info"
                              title="Live ATM pull resumes during market window (IST 09:10-15:30)."
                              className="text-muted"
                              style={{ cursor: 'help', fontSize: '0.78rem', userSelect: 'none' }}
                            >
                              ⓘ
                            </span>
                          ) : null}
                          <Form.Select
                            size="sm"
                            value={atmTarget.key}
                            style={{ width: '11rem' }}
                            onChange={(e) => setAtmTargetKey(e.target.value)}
                            aria-label="Scalper ATM underlying"
                          >
                            {SCALPER_ATM_TARGETS.map((t) => (
                              <option key={t.key} value={t.key}>
                                {t.label}
                              </option>
                            ))}
                          </Form.Select>
                        </div>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline-secondary"
                          disabled={!isLivePullWindow || atmReferenceLoading || atmLiveLoading}
                          onClick={() => {
                            void loadAtmReferences()
                            void refreshAtmLive()
                          }}
                        >
                          {atmReferenceLoading || atmLiveLoading ? 'Refreshing…' : 'Refresh ATM'}
                        </Button>
                      </div>
                      {atmError ? <Alert variant="warning" className="py-1 small mb-2">{atmError}</Alert> : null}
                      <div className="small text-muted mb-1">
                        Spot:{' '}
                        <span className="font-monospace fw-semibold">
                          {atmSnapshot.spotQuote ? atmSnapshot.spotQuote.lastPrice.toFixed(2) : '—'}
                        </span>{' '}
                        ATM:{' '}
                        <span className="font-monospace fw-semibold">
                          {atmSnapshot.atmStrike != null ? atmSnapshot.atmStrike.toFixed(2) : '—'}
                        </span>{' '}
                        {atmLastUpdatedAt ? `· updated ${formatLocalDateTime(atmLastUpdatedAt)}` : ''}
                      </div>
                      {atmSnapshot.chainRows.length > 0 ? (
                        <div className="table-responsive">
                          <Table size="sm" bordered hover className="mb-0 small align-middle">
                            <thead>
                              <tr>
                                <th className="text-end">Call LTP</th>
                                <th className="text-center">Strike</th>
                                <th className="text-start">Put LTP</th>
                              </tr>
                            </thead>
                            <tbody className="font-monospace">
                              {atmSnapshot.chainRows.map((row) => (
                                <tr key={`scalper-atm-${atmTarget.key}-${row.strike}`} className={row.isAtm ? 'table-warning' : undefined}>
                                  <td
                                    role={row.ceRow ? 'button' : undefined}
                                    className={`text-end ${row.ceRow ? 'cursor-pointer' : ''}`}
                                    onClick={() => row.ceRow && setSelected(row.ceRow)}
                                  >
                                    {row.ceQuote ? row.ceQuote.lastPrice.toFixed(2) : '—'}
                                  </td>
                                  <td className="text-center fw-semibold">{row.strike.toFixed(2)}{row.isAtm ? ' ★' : ''}</td>
                                  <td
                                    role={row.peRow ? 'button' : undefined}
                                    className={`text-start ${row.peRow ? 'cursor-pointer' : ''}`}
                                    onClick={() => row.peRow && setSelected(row.peRow)}
                                  >
                                    {row.peQuote ? row.peQuote.lastPrice.toFixed(2) : '—'}
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </Table>
                        </div>
                      ) : null}
                      <div className="mt-2 d-flex flex-wrap gap-2">
                        <Button
                          type="button"
                          size="sm"
                          variant="outline-primary"
                          disabled={!atmSpotRow}
                          onClick={() => atmSpotRow && setSelected(atmSpotRow)}
                        >
                          Spot chart
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline-secondary"
                          disabled={!atmSnapshot.ceRow}
                          onClick={() => atmSnapshot.ceRow && setSelected(atmSnapshot.ceRow)}
                        >
                          CE chart
                        </Button>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline-secondary"
                          disabled={!atmSnapshot.peRow}
                          onClick={() => atmSnapshot.peRow && setSelected(atmSnapshot.peRow)}
                        >
                          PE chart
                        </Button>
                      </div>
                    </div>
                  ) : null}
                  {selected && isZerodha ? (
                    <div className="border rounded border-secondary-subtle p-2 bg-body text-body mb-2">
                      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
                        <div className="small fw-semibold">{isSafeMode ? 'Safe scalper ticket' : 'Scalper ticket'}</div>
                        <div className="small text-muted font-monospace">
                          {selected.tradingsymbol} · {selected.exchange}
                        </div>
                      </div>
                      {tradeError ? <Alert variant="warning" className="py-1 small mb-2">{tradeError}</Alert> : null}
                      {tradeInfo ? <Alert variant="success" className="py-1 small mb-2">{tradeInfo}</Alert> : null}
                      {isSafeMode ? (
                        <Row className="g-2 mb-2">
                          <Col xs={12} md={6}>
                            <Form.Label className="small text-secondary mb-1">N points stop loss</Form.Label>
                            <Form.Control
                              size="sm"
                              type="number"
                              min={0.05}
                              step="0.05"
                              value={safeStopLossPoints}
                              onChange={(e) => setSafeStopLossPoints(e.target.value)}
                            />
                          </Col>
                          <Col xs={12} md={6}>
                            <Form.Label className="small text-secondary mb-1">M points sell trigger</Form.Label>
                            <Form.Control
                              size="sm"
                              type="number"
                              min={0.05}
                              step="0.05"
                              value={safeSellTriggerPoints}
                              onChange={(e) => setSafeSellTriggerPoints(e.target.value)}
                            />
                          </Col>
                          <Col xs={12}>
                            <div className="small text-secondary mb-1">Safe presets</div>
                            <div className="d-flex flex-wrap gap-1">
                              {SAFE_SCALPER_POINT_PRESETS.map((p) => (
                                <Button
                                  key={`safe-preset-${p.stop}-${p.trigger}`}
                                  size="sm"
                                  variant={
                                    safeStopLossPoints === p.stop && safeSellTriggerPoints === p.trigger
                                      ? 'secondary'
                                      : 'outline-secondary'
                                  }
                                  style={{ padding: '0.12rem 0.36rem', fontSize: '0.68rem', lineHeight: 1.1, borderRadius: '0.32rem' }}
                                  onClick={() => {
                                    setSafeStopLossPoints(p.stop)
                                    setSafeSellTriggerPoints(p.trigger)
                                  }}
                                >
                                  {p.label}
                                </Button>
                              ))}
                            </div>
                          </Col>
                        </Row>
                      ) : null}
                      <Row className="g-2 mb-2">
                        <Col xs={4}>
                          <Form.Label className="small text-secondary mb-1">Qty</Form.Label>
                          <Form.Control
                            size="sm"
                            type="number"
                            min={1}
                            step={1}
                            value={tradeQty}
                            onChange={(e) => setTradeQty(e.target.value)}
                          />
                        </Col>
                        <Col xs={4}>
                          <Form.Label className="small text-secondary mb-1">Product</Form.Label>
                          <Form.Select size="sm" value={tradeProduct} onChange={(e) => setTradeProduct(e.target.value as 'MIS' | 'NRML')}>
                            <option value="MIS">MIS</option>
                            <option value="NRML">NRML</option>
                          </Form.Select>
                        </Col>
                        <Col xs={4}>
                          <Form.Label className="small text-secondary mb-1">Type</Form.Label>
                          <Form.Select
                            size="sm"
                            value={tradeOrderType}
                            onChange={(e) => setTradeOrderType(e.target.value as ScalperTicketOrderType)}
                          >
                            {SCALPER_TICKET_ORDER_TYPES.map((ot) => (
                              <option key={`sc-order-type-${ot}`} value={ot}>
                                {ot}
                              </option>
                            ))}
                          </Form.Select>
                        </Col>
                      </Row>
                      <Row className="g-2 mb-2">
                        <Col xs={4}>
                          <Form.Label className="small text-secondary mb-1">Price</Form.Label>
                          <Form.Control
                            size="sm"
                            type="number"
                            min={0}
                            step="0.05"
                            value={tradePrice}
                            onChange={(e) => setTradePrice(e.target.value)}
                            placeholder="LIMIT/SL"
                          />
                        </Col>
                        <Col xs={4}>
                          <Form.Label className="small text-secondary mb-1">Sell trigger</Form.Label>
                          <Form.Control
                            size="sm"
                            type="number"
                            min={0}
                            step="0.05"
                            value={tradeTriggerPrice}
                            onChange={(e) => onTradeTriggerInputChange(e.target.value)}
                            placeholder="SL/SL-M"
                          />
                        </Col>
                        <Col xs={4}>
                          <Form.Label className="small text-secondary mb-1">Stop loss</Form.Label>
                          <Form.Control
                            size="sm"
                            type="number"
                            min={0}
                            step="0.05"
                            value={tradeStopLossPrice}
                            onChange={(e) => onTradeStopInputChange(e.target.value)}
                            placeholder="auto SL-M"
                          />
                        </Col>
                      </Row>
                      <InputGroup size="sm">
                        <Button variant="success" disabled={tradeBusy} onClick={() => void placeScalperOrder('BUY')}>
                          {tradeBusy ? 'Placing…' : 'Buy'}
                        </Button>
                        <Button variant="danger" disabled={tradeBusy} onClick={() => void placeScalperOrder('SELL')}>
                          {tradeBusy ? 'Placing…' : 'Sell'}
                        </Button>
                        <Button variant="outline-secondary" disabled={tradeBusy} onClick={() => void placeScalperOrder('EXIT')}>
                          {tradeBusy ? 'Placing…' : 'Exit'}
                        </Button>
                        <InputGroup.Text className="small text-muted">
                          {isSafeMode
                            ? `Safe mode: auto SL ${safeStopLossPoints || 'N'} pts, auto trigger ${safeSellTriggerPoints || 'M'} pts`
                            : lastEntrySide
                              ? `Last entry: ${lastEntrySide} · drag green/red lines on chart to adjust trigger/SL`
                              : 'SL: set Stop loss for protective order'}
                        </InputGroup.Text>
                      </InputGroup>
                    </div>
                  ) : null}
                  {selected ? (
                    <div
                      style={{
                        fontFamily: '"Trebuchet MS", "Segoe UI", Arial, sans-serif',
                        fontSize: '0.78rem',
                        lineHeight: 1.2,
                      }}
                    >
                      <TrendAnalysisMultiPanel
                        instrumentToken={selected.instrumentToken}
                        symbolLabel={`${selected.tradingsymbol} · ${selected.exchange}`}
                        historicalQueryExtra={trendHistoricalExtra}
                        variant="browseAlways"
                        embeddedIntervalSelector={{
                          heading: 'Trend analysis intervals',
                          options: SCALPER_TREND_INTERVAL_OPTIONS,
                          defaultSelectedIds: defaultTrendIntervals,
                        }}
                      />
                    </div>
                  ) : null}
                </Col>
              </Row>
            </Card.Body>
          </Card>
        </Col>
      </Row>
    </Layout>
  )
}
