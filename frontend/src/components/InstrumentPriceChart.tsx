/**
 * Zerodha-style OHLC chart using TradingView Lightweight Charts™ v5+
 * @see https://tradingview.github.io/lightweight-charts/
 */
import { Fragment, useLayoutEffect, useMemo, useRef } from 'react'
import type { IChartApi, IPriceLine, ISeriesApi, SeriesMarker, Time } from 'lightweight-charts'
import {
  BarSeries,
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  CrosshairMode,
  HistogramSeries,
  LineSeries,
  LineStyle,
  LineType,
} from 'lightweight-charts'
import type { UTCTimestamp } from 'lightweight-charts'
import { attachLinearTrendToChartPoints, LINEAR_CLOSE_TREND_COLOR } from '../utils/closeLinearTrend'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
import {
  barTimesUtc,
  lwBarsFromBars,
  lwCloseLine,
  lwCandlesFromBars,
  lwMaSeriesKeys,
  lwSingleValueSkipNull,
  lwTrendLine,
  lwVolumesFromBars,
} from '../utils/lightweightInstrumentData'
import type { MlPredictionLogEntry } from '../utils/mlPredictionHistory'
import { mapMlPredictionsPerTargetBar } from '../utils/mlPredictionHistory'
import type { ChartPointWithMa, MaLineVisibility } from '../utils/movingAverages'
import {
  DEFAULT_MA_LINE_VISIBILITY,
  MA_EMA_FAST_PERIOD,
  MA_EMA_SLOW_PERIOD,
  MA_LINE_COLORS,
  MA_SMA_PERIOD,
  SR_LINE_COLORS,
  SR_SWING_PERIOD,
} from '../utils/movingAverages'

export type InstrumentPriceChartGraphType = 'line' | 'bar' | 'candlestick'
export type InstrumentChartDensity = 'compact' | 'comfortable'

export type InstrumentPriceChartProps = {
  graphType: InstrumentPriceChartGraphType
  data: ChartPointWithMa[]
  maLineVisibility?: MaLineVisibility
  customEmaPeriod?: number | null
  livePrice?: number | null
  paperLastBuyPrice?: number | null
  paperBuyDataIndices?: readonly number[]
  mlPredictionEntries?: readonly MlPredictionLogEntry[]
  rechartsYDomain?: [number | string, number | string] | undefined
  density?: InstrumentChartDensity
  showVolume?: boolean
  newerGhostBars?: number
}

type MainKind = 'candlestick' | 'bar' | 'line'

type Internals = {
  chart: IChartApi
  mainKind: MainKind
  main: ISeriesApi<'Candlestick'> | ISeriesApi<'Bar'> | ISeriesApi<'Line'>
  histogram: ISeriesApi<'Histogram'> | null
  markers: ReturnType<typeof createSeriesMarkers<Time>>
  trendLine: ISeriesApi<'Line'> | null
  maSeries: Partial<Record<string, ISeriesApi<'Line'>>>
  priceLines: IPriceLine[]
}

const LIVE_LTP = '#38bdf8'
const PAPER_BUY_LINE = '#f59e0b'

function coerceFixedLwRange(dom: [unknown, unknown] | undefined): { from: number; to: number } | null {
  if (!dom || dom.length !== 2) return null
  const lo = coerceLwNum(dom[0])
  const hi = coerceLwNum(dom[1])
  if (lo == null || hi == null || lo >= hi) return null
  return { from: lo, to: hi }
}

function coerceLwNum(x: unknown): number | null {
  if (typeof x === 'number' && Number.isFinite(x)) return x
  if (typeof x === 'string') {
    const slug = x.toLowerCase().trim()
    if (slug === '' || slug === 'auto' || slug.includes('datamin') || slug.includes('datamax')) return null
    const n = Number(x)
    return Number.isFinite(n) ? n : null
  }
  return null
}

function mlDominantMarkerShape(entries: readonly MlPredictionLogEntry[]): 'arrowUp' | 'arrowDown' | 'square' {
  let up = 0
  let down = 0
  for (const e of entries) {
    if (e.direction === 'up') up++
    else if (e.direction === 'down') down++
  }
  if (up > down) return 'arrowUp'
  if (down > up) return 'arrowDown'
  return 'square'
}

function lwChartOptions(bg: string) {
  return {
    attributionLogo: false,
    autoSize: true,
    grid: {
      vertLines: { color: 'rgba(173,181,189,0.18)', style: LineStyle.Dotted },
      horzLines: { color: 'rgba(173,181,189,0.22)', style: LineStyle.LargeDashed },
    },
    layout: {
      background: { type: ColorType.Solid, color: bg },
      textColor: '#adb5bd',
      fontSize: 11,
    },
    rightPriceScale: {
      borderColor: 'rgba(173,181,189,0.35)',
      scaleMargins: { top: 0.06, bottom: 0.2 },
    },
    timeScale: {
      borderColor: 'rgba(173,181,189,0.35)',
      timeVisible: true,
      secondsVisible: false,
    },
    crosshair: { mode: CrosshairMode.MagnetOHLC },
  }
}

function graphToMainKind(g: InstrumentPriceChartGraphType): MainKind {
  if (g === 'line') return 'line'
  if (g === 'bar') return 'bar'
  return 'candlestick'
}

function addMain(chart: IChartApi, kind: MainKind, barCountEstimate: number): Internals['main'] {
  if (kind === 'line')
    return chart.addSeries(LineSeries, {
      color: '#0d6efd',
      lineWidth: 2,
      lineType: LineType.Simple,
      lastValueVisible: true,
      priceLineVisible: false,
    })

  if (kind === 'bar')
    return chart.addSeries(BarSeries, {
      thinBars: barCountEstimate > 260,
      upColor: 'rgba(13,110,253,0.92)',
      downColor: 'rgba(13,110,253,0.55)',
    })

  return chart.addSeries(CandlestickSeries, {
    upColor: '#22c55e',
    downColor: '#ef4444',
    borderUpColor: '#15803d',
    borderDownColor: '#b91c1c',
    wickUpColor: '#15803d',
    wickDownColor: '#b91c1c',
  })
}

function disposeInternals(st: Internals | null) {
  if (!st) return
  clearPriceLines(st)
  try {
    st.markers.detach()
  } catch {
    /* noop */
  }
  try {
    st.chart.remove()
  } catch {
    /* noop */
  }
}

function rebuildMainSeries(chart: IChartApi, st: Internals, want: MainKind, barEstimate: number) {
  clearPriceLines(st)
  try {
    st.markers.detach()
  } catch {
    /* noop */
  }
  chart.removeSeries(st.main)
  for (const s of Object.values(st.maSeries)) {
    removeSeriesQuiet(chart, s)
  }
  st.maSeries = {}
  removeSeriesQuiet(chart, st.trendLine)
  st.trendLine = null

  const main = addMain(chart, want, barEstimate)
  const markers = createSeriesMarkers(main, [])
  st.main = main
  st.markers = markers
  st.mainKind = want
}

function removeSeriesQuiet(chart: IChartApi, s: ISeriesApi<any> | null) {
  if (!s) return
  try {
    chart.removeSeries(s)
  } catch {
    /* noop */
  }
}

function clearPriceLines(st: Internals) {
  if (st.priceLines.length === 0) return
  for (const pl of st.priceLines) {
    try {
      st.main.removePriceLine(pl)
    } catch {
      /* noop */
    }
  }
  st.priceLines = []
}

function InstrumentChartCornerLegend({
  visibility,
  customEmaLinePeriod,
  graphType,
}: {
  visibility: MaLineVisibility
  customEmaLinePeriod: number | null
  graphType: InstrumentPriceChartGraphType
}) {
  const items: { key: string; label: string; color: string }[] = []
  if (
    visibility.showLinearCloseTrend &&
    (graphType === 'candlestick' || graphType === 'line')
  ) {
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

export function InstrumentPriceChart({
  graphType,
  data,
  maLineVisibility = DEFAULT_MA_LINE_VISIBILITY,
  customEmaPeriod = null,
  livePrice = null,
  paperLastBuyPrice = null,
  paperBuyDataIndices = [],
  mlPredictionEntries = [],
  rechartsYDomain,
  density = 'compact',
  showVolume = true,
  newerGhostBars = 0,
}: InstrumentPriceChartProps) {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const stRef = useRef<Internals | null>(null)
  const compactVol = density === 'compact'
  const ghost = Math.max(0, Math.floor(newerGhostBars))

  const times = useMemo(() => barTimesUtc(data, ghost), [data, ghost])
  const fixedY = useMemo(() => coerceFixedLwRange(rechartsYDomain), [rechartsYDomain])

  const bgMemo = typeof window !== 'undefined'
    ? window.getComputedStyle(document.documentElement).getPropertyValue('--bs-body-bg').trim() || '#0d1117'
    : '#0d1117'

  const maKeysMemo = useMemo(() => lwMaSeriesKeys(maLineVisibility, customEmaPeriod), [customEmaPeriod, maLineVisibility])

  const trendSeries = useMemo(() => {
    if (
      !maLineVisibility.showLinearCloseTrend ||
      (graphType !== 'candlestick' && graphType !== 'line')
    )
      return null
    return attachLinearTrendToChartPoints(data)
  }, [data, graphType, maLineVisibility.showLinearCloseTrend])
  useLayoutEffect(() => {
    const el = hostRef.current
    if (!el || data.length === 0) {
      disposeInternals(stRef.current)
      stRef.current = null
      return
    }

    const wantMain = graphToMainKind(graphType)

    let st = stRef.current
    if (!st?.chart) {
      disposeInternals(st)
      const chart = createChart(el, lwChartOptions(bgMemo))
      const histogram = showVolume
        ? chart.addSeries(HistogramSeries, {
            priceFormat: { type: 'volume' },
            priceScaleId: '',
          })
        : null
      histogram?.priceScale().applyOptions({
        scaleMargins: {
          top: compactVol ? 0.74 : 0.68,
          bottom: 0,
        },
      })

      const main = addMain(chart, wantMain, data.length + ghost)
      const markers = createSeriesMarkers(main, [])
      st = {
        chart,
        mainKind: wantMain,
        main,
        histogram,
        markers,
        trendLine: null,
        maSeries: {},
        priceLines: [],
      }
      stRef.current = st
      chart.applyOptions({
        localization: {
          timeFormatter: (t: Time) => lwTimeFmt(t),
          priceFormatter: (price: number) => {
            const a = Math.abs(price)
            const digits = a >= 100 ? 2 : 4
            return price.toLocaleString(undefined, {
              minimumFractionDigits: digits,
              maximumFractionDigits: digits,
            })
          },
        },
      })
    }

    /* ensure histogram visibility / margins */
    const stAlive = stRef.current!
    if (wantMain !== stAlive.mainKind) rebuildMainSeries(stAlive.chart, stAlive, wantMain, data.length + ghost)
    showVolumeApplied(stAlive, showVolume, compactVol)

    /* === DATA & overlays === */
    syncSeriesData(stAlive, {
      graphType,
      data,
      times,
      ghost,
      maKeysMemo,
      maLineVisibility,
      customEmaPeriod,
      trendSeries,
      fixedY,
      livePrice,
      paperLastBuyPrice,
      mlPredictionEntries,
      paperBuyDataIndices,
    })

    stAlive.chart.timeScale().fitContent()

    return () => undefined
    // intentionally drive off props that affect LW content
  }, [
    bgMemo,
    compactVol,
    customEmaPeriod,
    data,
    density,
    fixedY,
    ghost,
    graphType,
    livePrice,
    maKeysMemo,
    maLineVisibility,
    mlPredictionEntries,
    newerGhostBars,
    paperBuyDataIndices,
    paperLastBuyPrice,
    showVolume,
    times,
    trendSeries,
  ])

  useLayoutEffect(() => {
    return () => {
      disposeInternals(stRef.current)
      stRef.current = null
    }
  }, [])

  return (
    <div className="position-relative w-100 h-100 d-flex flex-column">
      <div className="flex-grow-1" style={{ minHeight: 0 }} ref={hostRef}>
        {data.length === 0 ? (
          <div className="d-flex align-items-center justify-content-center text-secondary small h-100">No candles.</div>
        ) : null}
      </div>
      <InstrumentChartCornerLegend
        visibility={maLineVisibility}
        customEmaLinePeriod={customEmaPeriod}
        graphType={graphType}
      />
    </div>
  )
}

function lwTimeFmt(t: Time): string {
  try {
    if (typeof t === 'number') return formatLocalDateTime(new Date((t as number) * 1000).toISOString())
    if (typeof t === 'object' && t != null && 'year' in t) {
      const bd = t as { year: number; month: number; day: number }
      const iso = `${bd.year}-${String(bd.month).padStart(2, '0')}-${String(bd.day).padStart(2, '0')}T12:00:00.000Z`
      return formatLocalDateTime(new Date(iso).toISOString())
    }
    if (typeof t === 'string') return formatLocalDateTime(new Date(`${t}T12:00:00.000Z`).toISOString())
  } catch {
    /* noop */
  }
  return String(t)
}

function showVolumeApplied(st: Internals, show: boolean, compact: boolean) {
  if (!st.histogram && show && st.chart) {
    const histogram = st.chart.addSeries(HistogramSeries, {
      priceFormat: { type: 'volume' },
      priceScaleId: '',
    })
    histogram.priceScale().applyOptions({
      scaleMargins: {
        top: compact ? 0.74 : 0.68,
        bottom: 0,
      },
    })
    st.histogram = histogram
  }

  if (st.histogram) {
    if (!show)
      try {
        st.chart.removeSeries(st.histogram)
        st.histogram = null
      } catch {
        st.histogram = null
      }
    else {
      st.histogram.applyOptions({ visible: true })
      st.histogram.priceScale().applyOptions({
        scaleMargins: {
          top: compact ? 0.74 : 0.68,
          bottom: 0,
        },
      })
    }
  }
}

type SyncPack = {
  graphType: InstrumentPriceChartGraphType
  data: ChartPointWithMa[]
  times: UTCTimestamp[]
  ghost: number
  maKeysMemo: Partial<Record<'sma20' | 'ema9' | 'ema21' | 'emaCustom' | 'srSupport' | 'srResistance', true>>
  maLineVisibility: MaLineVisibility
  customEmaPeriod: number | null
  trendSeries: ReturnType<typeof attachLinearTrendToChartPoints> | null
  fixedY: { from: number; to: number } | null
  livePrice?: number | null
  paperLastBuyPrice?: number | null
  mlPredictionEntries: readonly MlPredictionLogEntry[]
  paperBuyDataIndices: readonly number[]
}

function syncSeriesData(st: Internals, p: SyncPack) {
  clearPriceLines(st)

  purgeMaOverlay(st)

  const candles = lwCandlesFromBars(p.data, p.times)
  const bars = lwBarsFromBars(p.data, p.times)
  const vol = lwVolumesFromBars(p.data, p.times)

  if (st.mainKind === 'candlestick') {
    ;(st.main as ISeriesApi<'Candlestick'>).setData(candles)
  } else if (st.mainKind === 'bar') {
    ;(st.main as ISeriesApi<'Bar'>).setData(bars)
  } else {
    ;(st.main as ISeriesApi<'Line'>).setData(lwCloseLine(p.data, p.times))
  }

  st.histogram?.setData(vol)

  const chart = st.chart

  if (p.maKeysMemo.sma20) attachMa(chart, st, `sma20`, 'sma20', MA_LINE_COLORS.sma20, {}, p.data, p.times)
  if (p.maKeysMemo.ema9) attachMa(chart, st, `ema9`, 'ema9', MA_LINE_COLORS.ema9, {}, p.data, p.times)
  if (p.maKeysMemo.ema21) attachMa(chart, st, `ema21`, 'ema21', MA_LINE_COLORS.ema21, {}, p.data, p.times)

  if (p.maKeysMemo.emaCustom) {
    attachMa(chart, st, `cus`, `emaCustom`, MA_LINE_COLORS.emaCustom, {}, p.data, p.times)
  }

  if (p.maKeysMemo.srSupport) {
    attachMa(chart, st, `su`, 'srSupport', SR_LINE_COLORS.support, { lineStyle: LineStyle.Dashed }, p.data, p.times)
    attachMa(chart, st, `re`, 'srResistance', SR_LINE_COLORS.resistance, { lineStyle: LineStyle.Dashed }, p.data, p.times)
  }

  removeSeriesQuiet(chart, st.trendLine)
  st.trendLine = null
  if (p.maLineVisibility.showLinearCloseTrend && p.trendSeries && st.mainKind !== 'bar') {
    const trendLine = chart.addSeries(LineSeries, {
      color: LINEAR_CLOSE_TREND_COLOR,
      lineWidth: 2,
      lineStyle: LineStyle.LargeDashed,
      lastValueVisible: false,
      priceLineVisible: false,
    })
    trendLine.setData(lwTrendLine(p.trendSeries, p.times))
    st.trendLine = trendLine
  }

  const ps = st.main.priceScale()
  if (p.fixedY) {
    ps.setAutoScale(false)
    ps.setVisibleRange({ from: p.fixedY.from, to: p.fixedY.to })
  } else {
    ps.setAutoScale(true)
  }

  const markersArr: SeriesMarker<Time>[] = []
  const tgt = mapMlPredictionsPerTargetBar(p.mlPredictionEntries, p.data)
  for (let i = 0; i < p.data.length; i++) {
    const preds = tgt.get(i)
    if (!preds?.length) continue
    markersArr.push({
      time: p.times[i],
      position: 'aboveBar',
      shape: mlDominantMarkerShape(preds),
      color: '#94a3b8',
    })
  }

  for (const idx of p.paperBuyDataIndices) {
    if (!Number.isFinite(idx) || idx < 0 || idx >= p.data.length) continue
    markersArr.push({
      time: p.times[idx],
      position: 'belowBar',
      color: '#84cc16',
      shape: 'circle',
    })
  }
  st.markers.setMarkers(markersArr)

  if (typeof p.livePrice === 'number' && Number.isFinite(p.livePrice))
    st.priceLines.push(
      st.main.createPriceLine({
        price: p.livePrice,
        color: LIVE_LTP,
        lineWidth: 2,
        lineStyle: LineStyle.LargeDashed,
        axisLabelVisible: true,
        title: 'LTP',
      }),
    )
  if (typeof p.paperLastBuyPrice === 'number' && Number.isFinite(p.paperLastBuyPrice))
    st.priceLines.push(
      st.main.createPriceLine({
        price: p.paperLastBuyPrice,
        color: PAPER_BUY_LINE,
        lineWidth: 2,
        lineStyle: LineStyle.Dashed,
        axisLabelVisible: true,
        title: 'Last buy',
      }),
    )
}

function purgeMaOverlay(st: Internals) {
  for (const series of Object.values(st.maSeries)) {
    removeSeriesQuiet(st.chart, series)
  }
  st.maSeries = {}
}

function attachMa(
  chart: IChartApi,
  st: Internals,
  uiKey: string,
  ptKey:
    | keyof Pick<
        ChartPointWithMa,
        'sma20' | 'ema9' | 'ema21' | 'emaCustom' | 'srSupport' | 'srResistance'
      >,
  color: string,
  extras: Partial<Parameters<ISeriesApi<'Line'>['applyOptions']>[0]>,
  rows: ChartPointWithMa[],
  times: UTCTimestamp[],
) {
  const line = chart.addSeries(LineSeries, {
    priceLineVisible: false,
    lastValueVisible: false,
    color,
    lineWidth: 1,
    ...extras,
  })
  line.setData(lwSingleValueSkipNull(rows, times, ptKey) as Parameters<ISeriesApi<'Line'>['setData']>[0])
  st.maSeries[uiKey] = line
}