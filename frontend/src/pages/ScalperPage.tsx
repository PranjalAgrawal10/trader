import axios from 'axios'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
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
} from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import { InstrumentPriceChart } from '../components/InstrumentPriceChart'
import { Layout } from '../components/Layout'
import { TrendAnalysisMultiPanel } from '../components/TrendAnalysisMultiPanel'
import { useLiveMarketTick } from '../hooks/useLiveMarketTick'
import { useChartOlderBars } from '../hooks/useChartOlderBars'
import { CHART_INTERVALS, type ChartGraphType } from '../utils/kiteInstrumentChartShared'
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
  CUSTOM_EMA_PERIOD_MAX,
  CUSTOM_EMA_PERIOD_MIN,
  type ChartPointWithMa,
  type MaLineVisibility,
} from '../utils/movingAverages'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
import { isIstMarketLiveWindow } from '../utils/marketHours'

const SCALPER_TREND_INTERVAL_OPTIONS: readonly string[] = ['1m', '2m', '3m', '5m', '10m', '15m', '30m', '1h']
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
  const [broker, setBroker] = useState<BrokerStatusResponse | null>(null)

  const [selected, setSelected] = useState<KiteInstrumentRow | null>(null)
  const [interval, setInterval] = useState<ScalperInterval>('1m')
  const [rangePreset, setRangePreset] = useState<ScalperRange>('last3d')
  const [showVolume, setShowVolume] = useState(true)
  const [graphType, setGraphType] = useState<ChartGraphType>('candlestick')
  const [maLineVisibility, setMaLineVisibility] = useState<MaLineVisibility>(SCALPER_MA)
  const [customEmaPeriod, setCustomEmaPeriod] = useState<number>(CUSTOM_EMA_DEFAULT_PERIOD)
  const [trendIntervals, setTrendIntervals] = useState<string[]>(['1m', '3m', '5m', '15m'])

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
  const [atmSnapshot, setAtmSnapshot] = useState<ScalperAtmSnapshot>({
    spotQuote: null,
    optionExpiry: null,
    atmStrike: null,
    ceRow: null,
    peRow: null,
    ceQuote: null,
    peQuote: null,
    chainRows: [],
  })
  const [isLivePullWindow, setIsLivePullWindow] = useState<boolean>(() => isIstMarketLiveWindow())

  const isZerodha = broker?.connected === true && (broker?.provider ?? '').toLowerCase() === 'zerodha'
  const atmTarget = useMemo(
    () => SCALPER_ATM_TARGETS.find((t) => t.key === atmTargetKey) ?? SCALPER_ATM_TARGETS[0]!,
    [atmTargetKey],
  )

  const live = useLiveMarketTick(
    selected?.instrumentToken ?? null,
    isZerodha && selected != null && isLivePullWindow,
  )

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

      setAtmSnapshot({
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
      })
      setAtmLastUpdatedAt(new Date().toISOString())
    } catch (err) {
      setAtmError(problemDetail(err))
    } finally {
      setAtmLiveLoading(false)
    }
  }, [atmOptionRows, atmSnapshot, atmSpotRow, isLivePullWindow, isZerodha])

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

  const customEmaApplied = useMemo(
    () => effectiveCustomEmaPeriod(maLineVisibility, customEmaPeriod),
    [maLineVisibility, customEmaPeriod],
  )

  const displaySeriesWithCustom = useMemo(
    () => addCustomEmaToChartPoints(displaySeries, customEmaApplied),
    [displaySeries, customEmaApplied],
  )

  const trendHistoricalExtra = useMemo(() => scalperRangeQueryParams(rangePreset), [rangePreset])
  const trendIntervalsOrdered = useMemo(
    () => CHART_INTERVALS.filter((iv) => trendIntervals.includes(iv)),
    [trendIntervals],
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
        <div>
          <h1 className="h3 mb-0">Scalper</h1>
          <p className="text-secondary small mb-0">
            Tight-interval candles and live LTP for quick reads. Defaults to 3-day history with quick symbol search.
          </p>
        </div>
        <div className="d-flex flex-wrap align-items-center gap-2">
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
                    <div className="border rounded border-secondary-subtle p-2 mb-2">
                      <div className="small fw-semibold mb-2">Indicators</div>
                      <div className="d-flex flex-wrap gap-2 mb-2">
                        <Button size="sm" variant={graphType === 'candlestick' ? 'secondary' : 'outline-secondary'} onClick={() => setGraphType('candlestick')}>
                          Candles
                        </Button>
                        <Button size="sm" variant={graphType === 'line' ? 'secondary' : 'outline-secondary'} onClick={() => setGraphType('line')}>
                          Line
                        </Button>
                        <Button size="sm" variant={graphType === 'bar' ? 'secondary' : 'outline-secondary'} onClick={() => setGraphType('bar')}>
                          Bar
                        </Button>
                        <Button size="sm" variant={maLineVisibility.showSma20 ? 'secondary' : 'outline-secondary'} onClick={() => setMaLineVisibility((p) => ({ ...p, showSma20: !p.showSma20 }))}>
                          SMA 20
                        </Button>
                        <Button size="sm" variant={maLineVisibility.showEma9 ? 'secondary' : 'outline-secondary'} onClick={() => setMaLineVisibility((p) => ({ ...p, showEma9: !p.showEma9 }))}>
                          EMA 9
                        </Button>
                        <Button size="sm" variant={maLineVisibility.showEma21 ? 'secondary' : 'outline-secondary'} onClick={() => setMaLineVisibility((p) => ({ ...p, showEma21: !p.showEma21 }))}>
                          EMA 21
                        </Button>
                        <Button size="sm" variant={maLineVisibility.showCustomEma ? 'secondary' : 'outline-secondary'} onClick={() => setMaLineVisibility((p) => ({ ...p, showCustomEma: !p.showCustomEma }))}>
                          Custom EMA
                        </Button>
                        <Button size="sm" variant={maLineVisibility.showSupportResistance ? 'secondary' : 'outline-secondary'} onClick={() => setMaLineVisibility((p) => ({ ...p, showSupportResistance: !p.showSupportResistance }))}>
                          S/R
                        </Button>
                        <Button size="sm" variant={maLineVisibility.showLinearCloseTrend ? 'secondary' : 'outline-secondary'} onClick={() => setMaLineVisibility((p) => ({ ...p, showLinearCloseTrend: !p.showLinearCloseTrend }))}>
                          Trend LR
                        </Button>
                      </div>
                      <div className="d-flex flex-wrap align-items-center gap-2">
                        <Form.Label className="small mb-0">Custom EMA period</Form.Label>
                        <Form.Control
                          type="number"
                          size="sm"
                          style={{ width: '6.5rem' }}
                          min={CUSTOM_EMA_PERIOD_MIN}
                          max={CUSTOM_EMA_PERIOD_MAX}
                          step={1}
                          value={customEmaPeriod}
                          disabled={!maLineVisibility.showCustomEma}
                          onChange={(e) => {
                            const n = parseInt(e.target.value, 10)
                            if (!Number.isFinite(n)) return
                            setCustomEmaPeriod(Math.min(CUSTOM_EMA_PERIOD_MAX, Math.max(CUSTOM_EMA_PERIOD_MIN, n)))
                          }}
                        />
                        <span className="small text-muted">{CUSTOM_EMA_PERIOD_MIN}-{CUSTOM_EMA_PERIOD_MAX}</span>
                      </div>
                    </div>
                  ) : null}
                  {chartLoading && rawSeries.length === 0 ? (
                    <div className="text-center py-5 text-secondary">
                      <Spinner animation="border" className="me-2" />
                      Loading candles…
                    </div>
                  ) : selected && displaySeriesWithCustom.length > 0 ? (
                    <div style={{ height: 'min(58vh, 520px)', minHeight: '320px' }}>
                      <InstrumentPriceChart
                        graphType={graphType}
                        data={displaySeriesWithCustom}
                        maLineVisibility={maLineVisibility}
                        customEmaPeriod={customEmaApplied}
                        livePrice={live.lastPrice}
                        showVolume={showVolume}
                        newerGhostBars={0}
                        onNeedOlderBars={loadOlderBars}
                        canLoadOlderBars={canLoadOlderBars}
                        loadingOlderBars={loadingOlderBars}
                      />
                    </div>
                  ) : selected && !chartLoading ? (
                    <p className="text-muted small mb-0">No candle data returned for this range.</p>
                  ) : !selected ? (
                    <p className="text-muted small mb-0">Choose an instrument to load the chart.</p>
                  ) : null}
                </Col>
                <Col lg={5} xl={4}>
                  {isZerodha ? (
                    <div className="border rounded border-secondary-subtle p-2 bg-light text-dark mb-2">
                      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
                        <div className="d-flex flex-wrap align-items-center gap-2">
                          <div className="small fw-semibold">Live ATM chain</div>
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
                      {!isLivePullWindow ? (
                        <div className="small text-muted mb-1">Live ATM pull resumes during market window (IST 09:10-15:30).</div>
                      ) : null}
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
                  {selected ? (
                    <div className="border rounded border-secondary-subtle p-2 mb-2">
                      <div className="small fw-semibold mb-2">Trend analysis intervals</div>
                      <div className="d-flex flex-wrap gap-2">
                        {SCALPER_TREND_INTERVAL_OPTIONS.map((iv) => {
                          const active = trendIntervals.includes(iv)
                          return (
                            <Button
                              key={`scalper-trend-${iv}`}
                              size="sm"
                              variant={active ? 'secondary' : 'outline-secondary'}
                              onClick={() =>
                                setTrendIntervals((prev) =>
                                  prev.includes(iv) ? prev.filter((x) => x !== iv) : [...prev, iv],
                                )
                              }
                            >
                              {iv}
                            </Button>
                          )
                        })}
                      </div>
                    </div>
                  ) : null}
                  {selected && trendIntervalsOrdered.length > 0 ? (
                    <TrendAnalysisMultiPanel
                      instrumentToken={selected.instrumentToken}
                      symbolLabel={`${selected.tradingsymbol} · ${selected.exchange}`}
                      historicalQueryExtra={trendHistoricalExtra}
                      selectedIntervalsOrdered={trendIntervalsOrdered}
                      variant="browseAlways"
                    />
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
