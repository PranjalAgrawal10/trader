/**
 * Small non–OHLC plots on TradingView Lightweight Charts™ v5+.
 * @see https://tradingview.github.io/lightweight-charts/
 */
import { useLayoutEffect, useRef } from 'react'
import type { IChartApi, Time, UTCTimestamp } from 'lightweight-charts'
import {
  ColorType,
  createChart,
  CrosshairMode,
  HistogramSeries,
  LineSeries,
  LineStyle,
  LineType,
} from 'lightweight-charts'

const CAT_BASE_TIME = 946684800 as UTCTimestamp // Synthetic category axis origin (UTC s)

export type LwSyntheticBarRow = {
  key: string
  label: string
  value: number
  color: string
}

function lwShell(bg: string) {
  return {
    attributionLogo: false,
    autoSize: true,
    layout: {
      background: { type: ColorType.Solid, color: bg },
      textColor: '#adb5bd',
      fontSize: 11,
    },
    grid: {
      vertLines: { color: 'rgba(173,181,189,0.16)' },
      horzLines: { color: 'rgba(173,181,189,0.2)', style: LineStyle.LargeDashed },
    },
    leftPriceScale: { visible: false },
    rightPriceScale: { visible: false, borderVisible: false },
    timeScale: { visible: false, borderVisible: false, ticksVisible: true },
    crosshair: {
      mode: CrosshairMode.Normal,
      vertLine: { labelVisible: false },
      horzLine: { labelVisible: false },
    },
  }
}

function readBodyBg(): string {
  if (typeof window === 'undefined') return '#0d1117'
  return window.getComputedStyle(document.documentElement).getPropertyValue('--bs-body-bg').trim() || '#0d1117'
}

/** Histogram with synthetic timestamps; tick labels mapped from `label`. */
export function LwSyntheticHistogram({ rows, heightPx }: { rows: readonly LwSyntheticBarRow[]; heightPx: number }) {
  const ref = useRef<HTMLDivElement | null>(null)
  const chartRef = useRef<IChartApi | null>(null)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el || rows.length === 0) {
      chartRef.current?.remove()
      chartRef.current = null
      return
    }

    chartRef.current?.remove()
    const chart = createChart(el, lwShell(readBodyBg()))
    chartRef.current = chart

    const hs = chart.addSeries(HistogramSeries, {
      priceFormat: { type: 'price', precision: 2, minMove: 0.01 },
    })

    hs.setData(
      rows.map((r, i) => ({
        time: ((CAT_BASE_TIME as number) + i) as UTCTimestamp,
        value: r.value,
        color: r.color,
      })),
    )

    const labels = rows.map((r) => r.label)
    chart.applyOptions({
      timeScale: {
        tickMarkFormatter: (t: Time) => {
          const n = typeof t === 'number' ? t : NaN
          const idx = Math.round(n - (CAT_BASE_TIME as number))
          const s = labels[idx]
          return s && s.length > 10 ? s.slice(0, 9) + '…' : (s ?? '')
        },
      },
    })
    chart.timeScale().fitContent()

    return () => {
      chart.remove()
      chartRef.current = null
    }
  }, [rows])

  return <div ref={ref} style={{ height: heightPx, width: '100%' }} />
}

export function LwTimeLine({
  points,
  heightPx,
  stroke = '#fd7e14',
  dots = false,
  zeroBaseline = false,
}: {
  points: { timeMs: number; value: number }[]
  heightPx: number
  stroke?: string
  dots?: boolean
  zeroBaseline?: boolean
}) {
  const ref = useRef<HTMLDivElement | null>(null)
  const chartRef = useRef<IChartApi | null>(null)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el || points.length === 0) {
      chartRef.current?.remove()
      chartRef.current = null
      return
    }

    chartRef.current?.remove()
    const chart = createChart(el, lwShell(readBodyBg()))
    chartRef.current = chart

    const ln = chart.addSeries(LineSeries, {
      color: stroke,
      lineWidth: 2,
      lineType: LineType.Simple,
      lastValueVisible: true,
      pointMarkersVisible: dots,
      priceLineVisible: false,
    })
    ln.setData(
      points.map((p) => ({
        time: Math.floor(p.timeMs / 1000) as UTCTimestamp,
        value: p.value,
      })),
    )

    if (zeroBaseline)
      ln.createPriceLine({
        price: 0,
        color: '#6c757d',
        lineWidth: 1,
        lineStyle: LineStyle.LargeDashed,
        axisLabelVisible: false,
        title: '0',
      })

    chart.timeScale().fitContent()

    return () => {
      chart.remove()
      chartRef.current = null
    }
  }, [dots, heightPx, points, stroke, zeroBaseline])

  return <div ref={ref} style={{ height: heightPx, width: '100%' }} />
}
