import { Fragment, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { CHART_RIGHT_EDGE_GAP_FRACT } from '../constants/chartLayout'
import type { ChartPointWithMaAndTrend } from '../utils/closeLinearTrend'
import { attachLinearTrendToChartPoints, LINEAR_CLOSE_TREND_COLOR } from '../utils/closeLinearTrend'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'
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
import { MlDirectionRibbonHtml, MlDirectionRibbonSvg } from './MlDirectionRibbon'
import type { MlPredictionLogEntry } from '../utils/mlPredictionHistory'
import { groupMlPredictionsByChartBarIndex, mapMlPredictionsPerTargetBar } from '../utils/mlPredictionHistory'

const PAD = { top: 6, right: 8, left: 52 }

/** Reserve space beneath time labels so price + volume both fit vertically. */
const LABEL_BAND_PX = 24

const VOL_PRICE_GAP_PX = 4

/** Padding inside the volume histogram band (pixels). */
const VOL_BAR_INSET_TOP = 4
const VOL_BAR_INSET_BOTTOM = 2

/** When zoomed to few bars, avoid stretching candles across the full plot — keep a max pitch and center the cluster (latest bar in the middle). */
const MAX_CANDLE_SLOT_PX = 28

/** Bullish / bearish candle colors (typical trading terminal green / red). */
const CANDLE = {
  upFill: '#22c55e',
  upStroke: '#15803d',
  downFill: '#ef4444',
  downStroke: '#b91c1c',
  grid: 'rgba(173, 181, 189, 0.35)',
  text: '#adb5bd',
}

/** Horizontal guide for streamed last price (SignalR ticks), drawn above candles. */
const LIVE_LTP_LINE = '#38bdf8'

/** Latest demo paper BUY fill (when position open), distinct from live LTP. */
const PAPER_LAST_BUY_LINE = '#f59e0b'

function formatChartLivePriceLabel(p: number): string {
  if (!Number.isFinite(p)) return ''
  const a = Math.abs(p)
  const digits = a >= 100 ? 2 : a >= 1 ? 4 : 4
  return p.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: digits })
}

type MaKey = keyof Pick<
  ChartPointWithMa,
  'sma20' | 'ema9' | 'ema21' | 'emaCustom' | 'srSupport' | 'srResistance'
>

function buildTrendPath(
  points: ChartPointWithMaAndTrend[],
  clusterStartX: number,
  slotW: number,
  yPrice: (p: number) => number,
): string {
  let d = ''
  let penUp = true
  for (let i = 0; i < points.length; i++) {
    const v = points[i].trendLine
    if (v == null || !Number.isFinite(v)) {
      penUp = true
      continue
    }
    const cx = clusterStartX + i * slotW + slotW / 2
    const cy = yPrice(v)
    d += penUp ? `M ${cx} ${cy}` : ` L ${cx} ${cy}`
    penUp = false
  }
  return d
}

function buildMaPath(
  key: MaKey,
  data: ChartPointWithMa[],
  clusterStartX: number,
  slotW: number,
  yPrice: (p: number) => number,
): string {
  let d = ''
  let penUp = true
  for (let i = 0; i < data.length; i++) {
    const v = data[i][key]
    if (v == null || !Number.isFinite(v)) {
      penUp = true
      continue
    }
    const cx = clusterStartX + i * slotW + slotW / 2
    const cy = yPrice(v)
    d += penUp ? `M ${cx} ${cy}` : ` L ${cx} ${cy}`
    penUp = false
  }
  return d
}

/** X-axis time labels: a few ticks across the window (each uses {@link formatLocalDateTime}). */
function computeXTickIndices(n: number, targetSlots: number): number[] {
  if (n <= 0) return []
  if (n === 1) return [0]
  const slots = Math.min(n, Math.max(2, targetSlots))
  if (n <= slots) return Array.from({ length: n }, (_, i) => i)
  const out: number[] = []
  for (let k = 0; k < slots; k++) {
    const i = Math.round((k * (n - 1)) / (slots - 1))
    if (out.length === 0 || out[out.length - 1] !== i) out.push(i)
  }
  return out
}

function useContainerPixelSize<T extends HTMLElement>() {
  const ref = useRef<T | null>(null)
  const [size, setSize] = useState({ w: 0, h: 0 })

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return

    const update = () => setSize({ w: el.clientWidth, h: el.clientHeight })

    const ro = new ResizeObserver(update)
    ro.observe(el)
    update()

    return () => ro.disconnect()
  }, [])

  return { ref, ...size }
}

export function CandlestickChart({
  data,
  maLineVisibility = DEFAULT_MA_LINE_VISIBILITY,
  customEmaPeriod = null,
  livePrice = null,
  paperLastBuyPrice = null,
  paperBuyDataIndices = [],
  mlPredictionEntries = [],
  newerGhostSlots = 0,
}: {
  data: ChartPointWithMa[]
  maLineVisibility?: MaLineVisibility
  /** Period label in the corner legend (values come from <code>data[].emaCustom</code>). */
  customEmaPeriod?: number | null
  /** Draw a horizontal LTP guide when streamed quotes are available (e.g. market hub ticks). */
  livePrice?: number | null
  /** Horizontal guide at the latest demo BUY fill when a paper long is open. */
  paperLastBuyPrice?: number | null
  /** 0-based indices into <code>data</code>: vertical markers for OPEN demo paper buys (FIFO until sold). */
  paperBuyDataIndices?: readonly number[]
  /** Classic + LightGBM price-direction rows; ribbons above bars where predictions target that interval. */
  mlPredictionEntries?: readonly MlPredictionLogEntry[]
  /** Empty candle slots on the right (pan “past” newest while zoomed). */
  newerGhostSlots?: number
}) {
  const { ref, w, h } = useContainerPixelSize<HTMLDivElement>()

  const trendSeries = useMemo(
    () => (maLineVisibility.showLinearCloseTrend ? attachLinearTrendToChartPoints(data) : null),
    [data, maLineVisibility.showLinearCloseTrend],
  )

  const mlPredictionsByDataIndex = useMemo(
    () => groupMlPredictionsByChartBarIndex(mlPredictionEntries, data),
    [mlPredictionEntries, data],
  )

  const mlTargetBySliceIndex = useMemo(
    () => mapMlPredictionsPerTargetBar(mlPredictionEntries, data),
    [mlPredictionEntries, data],
  )

  const layout = useMemo(() => {
    if (data.length === 0 || w < 40 || h < 40) return null

    const rightGutterPx = w * CHART_RIGHT_EDGE_GAP_FRACT
    const plotW = w - PAD.left - PAD.right - rightGutterPx
    const plotRightX = PAD.left + plotW
    const innerBottomY = h - LABEL_BAND_PX
    if (plotW < 10 || innerBottomY < PAD.top + 70) return null

    const MIN_PRICE_PANE_PX = 56
    const MIN_VOL_PANE_PX = 28
    const innerAvail = innerBottomY - PAD.top - VOL_PRICE_GAP_PX
    let volPaneH = Math.max(
      MIN_VOL_PANE_PX,
      Math.min(88, Math.round(innerAvail * 0.195)),
    )
    let priceBottomY = innerBottomY - VOL_PRICE_GAP_PX - volPaneH
    const priceTopY = PAD.top
    let pricePlotH = priceBottomY - priceTopY
    if (pricePlotH < MIN_PRICE_PANE_PX) {
      volPaneH = Math.max(MIN_VOL_PANE_PX, innerAvail - VOL_PRICE_GAP_PX - MIN_PRICE_PANE_PX)
      priceBottomY = innerBottomY - VOL_PRICE_GAP_PX - volPaneH
      pricePlotH = priceBottomY - priceTopY
    }
    const volumeBottomY = innerBottomY
    const volumeTopY = innerBottomY - volPaneH

    let volMax = 0
    for (const c of data) {
      if (Number.isFinite(c.volume)) volMax = Math.max(volMax, c.volume)
    }
    volMax = Math.max(volMax, 1)

    let min = Infinity
    let max = -Infinity
    for (const c of data) {
      min = Math.min(min, c.low)
      max = Math.max(max, c.high)
    }
    if (maLineVisibility.showLinearCloseTrend && trendSeries && trendSeries.length > 0) {
      for (const c of trendSeries) {
        const t = c.trendLine
        if (t != null && Number.isFinite(t)) {
          min = Math.min(min, t)
          max = Math.max(max, t)
        }
      }
    }
    for (const c of data) {
      if (maLineVisibility.showSma20) {
        const v = c.sma20
        if (v != null && Number.isFinite(v)) {
          min = Math.min(min, v)
          max = Math.max(max, v)
        }
      }
      if (maLineVisibility.showEma9) {
        const v = c.ema9
        if (v != null && Number.isFinite(v)) {
          min = Math.min(min, v)
          max = Math.max(max, v)
        }
      }
      if (maLineVisibility.showEma21) {
        const v = c.ema21
        if (v != null && Number.isFinite(v)) {
          min = Math.min(min, v)
          max = Math.max(max, v)
        }
      }
      if (maLineVisibility.showCustomEma) {
        const v = c.emaCustom
        if (v != null && Number.isFinite(v)) {
          min = Math.min(min, v)
          max = Math.max(max, v)
        }
      }
      if (maLineVisibility.showSupportResistance) {
        const s = c.srSupport
        const r = c.srResistance
        if (s != null && Number.isFinite(s)) {
          min = Math.min(min, s)
          max = Math.max(max, s)
        }
        if (r != null && Number.isFinite(r)) {
          min = Math.min(min, r)
          max = Math.max(max, r)
        }
      }
    }
    const lp = livePrice
    if (lp != null && Number.isFinite(lp)) {
      min = Math.min(min, lp)
      max = Math.max(max, lp)
    }
    const pb = paperLastBuyPrice
    if (pb != null && Number.isFinite(pb)) {
      min = Math.min(min, pb)
      max = Math.max(max, pb)
    }
    if (!Number.isFinite(min) || !Number.isFinite(max)) return null
    if (min === max) {
      const pad = min === 0 ? 1 : Math.abs(min) * 0.001
      min -= pad
      max += pad
    }

    const yPrice = (p: number) => priceTopY + ((max - p) / (max - min)) * pricePlotH
    const n = data.length

    const ghostSlots = Math.max(0, Math.min(512, Math.floor(newerGhostSlots)))
    const slotGuess = n > 0 ? Math.min(plotW / n, MAX_CANDLE_SLOT_PX) : MAX_CANDLE_SLOT_PX
    const ghostReservePx =
      ghostSlots > 0 && n > 0 ? Math.min(Math.max(0, plotW - 40), ghostSlots * slotGuess) : 0
    const candlePlotW = Math.max(32, plotW - ghostReservePx)

    const naturalSlotW = candlePlotW / n
    let slotW = naturalSlotW
    let clusterStartX = PAD.left
    if (naturalSlotW > MAX_CANDLE_SLOT_PX) {
      slotW = MAX_CANDLE_SLOT_PX
      const clusterW = n * slotW
      if (clusterW > candlePlotW) {
        slotW = candlePlotW / n
        clusterStartX = PAD.left
      } else {
        clusterStartX = PAD.left + (candlePlotW - clusterW) / 2
      }
    }
    const bodyW = Math.min(Math.max(1, slotW * 0.65), 14)

    const pathSma = buildMaPath('sma20', data, clusterStartX, slotW, yPrice)
    const pathEma9 = buildMaPath('ema9', data, clusterStartX, slotW, yPrice)
    const pathEma21 = buildMaPath('ema21', data, clusterStartX, slotW, yPrice)
    const pathEmaCustom = buildMaPath('emaCustom', data, clusterStartX, slotW, yPrice)
    const pathSrSupport = buildMaPath('srSupport', data, clusterStartX, slotW, yPrice)
    const pathSrResistance = buildMaPath('srResistance', data, clusterStartX, slotW, yPrice)

    const pathTrend =
      maLineVisibility.showLinearCloseTrend && trendSeries && trendSeries.length > 0
        ? buildTrendPath(trendSeries, clusterStartX, slotW, yPrice)
        : ''

    const targetXTicks = Math.min(6, Math.max(2, Math.floor(plotW / 72)))
    const xTickIndices = computeXTickIndices(n, targetXTicks)
    const xTicks = xTickIndices.map((i) => ({
      i,
      cx: clusterStartX + i * slotW + slotW / 2,
      label: formatLocalDateTime(data[i].t),
    }))

    const labelY = h - LABEL_BAND_PX + 13

    return {
      plotW,
      pricePlotH,
      priceTopY,
      priceBottomY,
      volumeTopY,
      volumeBottomY,
      volMax,
      min,
      max,
      yPrice,
      n,
      clusterStartX,
      slotW,
      bodyW,
      pathSma,
      pathEma9,
      pathEma21,
      pathEmaCustom,
      pathSrSupport,
      pathSrResistance,
      pathTrend,
      xTicks,
      labelY,
      plotRightX,
    }
  }, [data, w, h, maLineVisibility, trendSeries, customEmaPeriod, livePrice, paperLastBuyPrice, newerGhostSlots])

  const yTicks = useMemo(() => {
    if (!layout) return []
    const { min, max } = layout
    const mid = (min + max) / 2
    return [max, mid, min].filter((v, i, a) => a.findIndex((x) => Math.abs(x - v) < 1e-9) === i)
  }, [layout])

  const [hover, setHover] = useState<{ idx: number; tipX: number; tipY: number } | null>(null)

  useLayoutEffect(() => {
    setHover(null)
  }, [data])

  if (data.length === 0) return null

  const livePriceShown =
    livePrice != null && Number.isFinite(livePrice) ? (livePrice as number) : null
  const paperLastBuyShown =
    paperLastBuyPrice != null && Number.isFinite(paperLastBuyPrice)
      ? (paperLastBuyPrice as number)
      : null

  return (
    <div ref={ref} className="w-100 h-100 position-relative">
      {layout ? (
        <svg width={w} height={h} className="d-block" role="img" aria-label="OHLC candlestick chart with volume, overlays, and optional live last-price line">
          {yTicks.map((tp) => (
            <g key={tp}>
              <line
                x1={PAD.left}
                x2={layout.plotRightX}
                y1={layout.yPrice(tp)}
                y2={layout.yPrice(tp)}
                stroke={CANDLE.grid}
                strokeDasharray="4 4"
              />
              <text
                x={4}
                y={layout.yPrice(tp) + 4}
                fill={CANDLE.text}
                fontSize={10}
                style={{ userSelect: 'none' }}
              >
                {Number.isFinite(tp) ? tp.toFixed(2) : ''}
              </text>
            </g>
          ))}

          {data.map((c, i) => {
            const cx = layout.clusterStartX + i * layout.slotW + layout.slotW / 2
            const vn = Number.isFinite(c.volume) && c.volume >= 0 ? c.volume : 0
            const vSpan =
              layout.volumeBottomY - layout.volumeTopY - VOL_BAR_INSET_TOP - VOL_BAR_INSET_BOTTOM
            const barFullH =
              layout.volMax > 0 ? (vn / layout.volMax) * Math.max(vSpan, 1) : 0
            const barH = Math.max(barFullH, vn > 0 ? 1.5 : 0)
            const barTop =
              vn > 0
                ? layout.volumeBottomY - VOL_BAR_INSET_BOTTOM - barH
                : layout.volumeBottomY - VOL_BAR_INSET_BOTTOM
            const barW = Math.min(Math.max(1.5, layout.slotW * 0.54), 12)
            const bullish = c.close >= c.open

            return (
              <rect
                key={`vol-${c.t}-${c.idx}`}
                x={cx - barW / 2}
                y={barTop}
                width={barW}
                height={Math.max(barH, 0)}
                fill={
                  vn > 0
                    ? bullish
                      ? 'rgba(34, 197, 94, 0.68)'
                      : 'rgba(239, 68, 68, 0.68)'
                    : 'transparent'
                }
                stroke="transparent"
                style={{ pointerEvents: 'none' }}
              >
                <title>{`Vol ${vn.toLocaleString()}`}</title>
              </rect>
            )
          })}

          {data.map((c, i) => {
            const cx = layout.clusterStartX + i * layout.slotW + layout.slotW / 2
            const yHi = layout.yPrice(c.high)
            const yLo = layout.yPrice(c.low)
            const yOpen = layout.yPrice(c.open)
            const yClose = layout.yPrice(c.close)
            const top = Math.min(yOpen, yClose)
            const bot = Math.max(yOpen, yClose)
            const bullish = c.close >= c.open
            const fill = bullish ? CANDLE.upFill : CANDLE.downFill
            const stroke = bullish ? CANDLE.upStroke : CANDLE.downStroke
            const bodyH = Math.max(bot - top, 1)

            return (
              <g key={`${c.t}-${c.idx}`} style={{ pointerEvents: 'none' }}>
                <title>{c.ohlc}</title>
                <line x1={cx} x2={cx} y1={yHi} y2={yLo} stroke={stroke} strokeWidth={1} />
                <rect
                  x={cx - layout.bodyW / 2}
                  y={top}
                  width={layout.bodyW}
                  height={bodyH}
                  fill={fill}
                  stroke={stroke}
                  strokeWidth={1}
                />
              </g>
            )
          })}

          {layout.pathSrResistance && maLineVisibility.showSupportResistance ? (
            <path
              d={layout.pathSrResistance}
              fill="none"
              stroke={SR_LINE_COLORS.resistance}
              strokeWidth={1.25}
              strokeDasharray="4 3"
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}
          {layout.pathSrSupport && maLineVisibility.showSupportResistance ? (
            <path
              d={layout.pathSrSupport}
              fill="none"
              stroke={SR_LINE_COLORS.support}
              strokeWidth={1.25}
              strokeDasharray="4 3"
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}

          {layout.pathEma21 && maLineVisibility.showEma21 ? (
            <path
              d={layout.pathEma21}
              fill="none"
              stroke={MA_LINE_COLORS.ema21}
              strokeWidth={1.5}
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}
          {layout.pathEma9 && maLineVisibility.showEma9 ? (
            <path
              d={layout.pathEma9}
              fill="none"
              stroke={MA_LINE_COLORS.ema9}
              strokeWidth={1.5}
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}
          {layout.pathSma && maLineVisibility.showSma20 ? (
            <path
              d={layout.pathSma}
              fill="none"
              stroke={MA_LINE_COLORS.sma20}
              strokeWidth={1.5}
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}
          {layout.pathEmaCustom && maLineVisibility.showCustomEma && customEmaPeriod != null && customEmaPeriod >= 2 ? (
            <path
              d={layout.pathEmaCustom}
              fill="none"
              stroke={MA_LINE_COLORS.emaCustom}
              strokeWidth={1.5}
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}
          {layout.pathTrend && maLineVisibility.showLinearCloseTrend ? (
            <path
              d={layout.pathTrend}
              fill="none"
              stroke={LINEAR_CLOSE_TREND_COLOR}
              strokeWidth={2}
              strokeDasharray="6 4"
              strokeLinecap="round"
              strokeLinejoin="round"
              style={{ pointerEvents: 'none' }}
            />
          ) : null}

          {mlTargetBySliceIndex.size > 0 && layout
            ? data.map((c, i) => {
                const preds = mlTargetBySliceIndex.get(i)
                if (!preds?.length) return null
                const cx = layout.clusterStartX + i * layout.slotW + layout.slotW / 2
                const yHi = layout.yPrice(c.high)
                const yy = Math.max(layout.priceTopY + 7, yHi - 10)
                const iconPx = Math.min(8, Math.max(5.5, layout.slotW * 0.26))
                const fh = Math.round(iconPx + 5)
                return (
                  <MlDirectionRibbonSvg
                    key={`ml-tgt-${c.t}-${c.idx}`}
                    cx={cx}
                    yTop={yy - fh}
                    entries={preds}
                    iconPx={iconPx}
                  />
                )
              })
            : null}
          {paperBuyDataIndices.length > 0
            ? paperBuyDataIndices.map((di, k) => {
                if (!layout || di < 0 || di >= data.length) return null
                const cx = layout.clusterStartX + di * layout.slotW + layout.slotW / 2
                return (
                  <g key={`demo-paper-buy-${di}-${k}`} style={{ pointerEvents: 'none' }}>
                    <title>Open demo BUY (contracts close FIFO on sells)</title>
                    <line
                      x1={cx}
                      x2={cx}
                      y1={layout.priceTopY}
                      y2={layout.volumeBottomY}
                      stroke="#84cc16"
                      strokeWidth={1.35}
                      strokeDasharray="5 5"
                      opacity={0.92}
                    />
                  </g>
                )
              })
            : null}
          {paperLastBuyShown != null && layout ? (
            <g style={{ pointerEvents: 'none' }}>
              <title>{`Last demo BUY ${formatChartLivePriceLabel(paperLastBuyShown)}`}</title>
              <line
                x1={PAD.left}
                x2={layout.plotRightX}
                y1={layout.yPrice(paperLastBuyShown)}
                y2={layout.yPrice(paperLastBuyShown)}
                stroke={PAPER_LAST_BUY_LINE}
                strokeWidth={1.5}
                strokeDasharray="4 6"
                strokeLinecap="round"
              />
              <text
                x={PAD.left + 4}
                y={layout.yPrice(paperLastBuyShown) - 4}
                fill={PAPER_LAST_BUY_LINE}
                fontSize={10}
                fontWeight={700}
                textAnchor="start"
                style={{
                  userSelect: 'none',
                  filter:
                    'drop-shadow(1px 0 0 rgb(33 37 41 / 85%)) drop-shadow(-1px 0 0 rgb(33 37 41 / 85%)) drop-shadow(0 1px 0 rgb(33 37 41 / 85%))',
                }}
              >
                Last buy {formatChartLivePriceLabel(paperLastBuyShown)}
              </text>
            </g>
          ) : null}
          {livePriceShown != null && layout ? (
            <g style={{ pointerEvents: 'none' }}>
              <title>{`Live LTP ${formatChartLivePriceLabel(livePriceShown)}`}</title>
              <line
                x1={PAD.left}
                x2={layout.plotRightX}
                y1={layout.yPrice(livePriceShown)}
                y2={layout.yPrice(livePriceShown)}
                stroke={LIVE_LTP_LINE}
                strokeWidth={1.65}
                strokeDasharray="6 7"
                strokeLinecap="round"
              />
              <text
                x={layout.plotRightX - 4}
                y={layout.yPrice(livePriceShown) - 4}
                fill={LIVE_LTP_LINE}
                fontSize={10}
                fontWeight={700}
                textAnchor="end"
                style={{
                  userSelect: 'none',
                  filter:
                    'drop-shadow(1px 0 0 rgb(33 37 41 / 85%)) drop-shadow(-1px 0 0 rgb(33 37 41 / 85%)) drop-shadow(0 1px 0 rgb(33 37 41 / 85%))',
                }}
              >
                LTP {formatChartLivePriceLabel(livePriceShown)}
              </text>
            </g>
          ) : null}
          {hover ? (
            <line
              x1={layout.clusterStartX + hover.idx * layout.slotW + layout.slotW / 2}
              x2={layout.clusterStartX + hover.idx * layout.slotW + layout.slotW / 2}
              y1={layout.priceTopY}
              y2={layout.volumeBottomY}
              stroke="rgba(248, 249, 250, 0.35)"
              strokeWidth={1}
              pointerEvents="none"
            />
          ) : null}
          <line
            x1={PAD.left}
            x2={layout.plotRightX}
            y1={layout.priceBottomY}
            y2={layout.priceBottomY}
            stroke={CANDLE.grid}
            strokeWidth={1}
            pointerEvents="none"
          />
          <text
            x={4}
            y={layout.volumeTopY + 10}
            fill={CANDLE.text}
            fontSize={9}
            style={{ userSelect: 'none' }}
          >
            Vol
          </text>
          {layout.xTicks.map((xt) => (
            <text
              key={`xt-${xt.i}-${xt.label}`}
              x={xt.cx}
              y={layout.labelY}
              fill={CANDLE.text}
              fontSize={9}
              textAnchor="middle"
              style={{ userSelect: 'none' }}
            >
              {xt.label}
            </text>
          ))}
          <rect
            x={PAD.left}
            y={layout.priceTopY}
            width={layout.plotW}
            height={layout.volumeBottomY - layout.priceTopY}
            fill="transparent"
            style={{ cursor: 'crosshair' }}
            onMouseMove={(e) => {
              const svg = e.currentTarget.ownerSVGElement
              if (!svg || !ref.current) return
              const svgR = svg.getBoundingClientRect()
              const sx = ((e.clientX - svgR.left) / Math.max(svgR.width, 1)) * w
              const rel = sx - layout.clusterStartX
              const idx = Math.max(0, Math.min(data.length - 1, Math.floor(rel / layout.slotW)))
              const box = ref.current.getBoundingClientRect()
              setHover({
                idx,
                tipX: e.clientX - box.left,
                tipY: e.clientY - box.top,
              })
            }}
            onMouseLeave={() => setHover(null)}
          />
        </svg>
      ) : (
        <div className="d-flex align-items-center justify-content-center text-secondary small h-100">
          Resizing…
        </div>
      )}
      {layout && hover ? (
        <div
          className="position-absolute rounded border border-secondary py-1 px-2 shadow-sm"
          style={{
            left: Math.min(Math.max(8, hover.tipX + 14), Math.max(8, layout.plotRightX - 24)),
            top: Math.max(8, hover.tipY - 8),
            maxWidth: 208,
            zIndex: 6,
            background: '#212529',
            color: '#f8f9fa',
            fontSize: '0.72rem',
            lineHeight: 1.35,
            pointerEvents: 'none',
          }}
        >
          {(() => {
            const p = data[hover.idx]
            return (
              <>
                <div className="text-white-50 small mb-1">{formatLocalDateTime(p.t)}</div>
                <div className="font-monospace">
                  O {p.open} · H {p.high} · L {p.low} · C {p.close}
                </div>
                <div className="text-secondary small">
                  Vol{' '}
                  {Number.isFinite(Number(p.volume)) ? Number(p.volume).toLocaleString() : '—'}
                </div>
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
                customEmaPeriod != null &&
                customEmaPeriod >= 2 &&
                p.emaCustom != null ? (
                  <div className="font-monospace" style={{ color: MA_LINE_COLORS.emaCustom }}>
                    EMA{customEmaPeriod} {p.emaCustom.toFixed(4)}
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
                {maLineVisibility.showLinearCloseTrend && trendSeries && trendSeries[hover.idx]?.trendLine != null ? (
                  <div className="font-monospace mt-1" style={{ color: LINEAR_CLOSE_TREND_COLOR }}>
                    Trend LR {Number(trendSeries[hover.idx].trendLine).toFixed(4)}
                  </div>
                ) : null}
                {(() => {
                  const refPreds = mlPredictionsByDataIndex.get(hover.idx)
                  const tgtPreds = mlTargetBySliceIndex.get(hover.idx)
                  if ((!refPreds || refPreds.length === 0) && (!tgtPreds || tgtPreds.length === 0))
                    return null
                  return (
                    <div className="mt-2 pt-1 border-top border-secondary border-opacity-50 d-flex flex-column gap-1 align-items-start">
                      {refPreds && refPreds.length > 0 ? (
                        <MlDirectionRibbonHtml entries={refPreds} iconPx={8} />
                      ) : null}
                      {tgtPreds && tgtPreds.length > 0 ? (
                        <MlDirectionRibbonHtml entries={tgtPreds} iconPx={8} />
                      ) : null}
                    </div>
                  )
                })()}
              </>
            )
          })()}
        </div>
      ) : null}
      {layout ? (
        <div
          className="position-absolute small text-secondary"
          style={{ right: 8, top: 4, fontSize: '0.65rem', pointerEvents: 'none' }}
        >
          {(() => {
            const items: { key: string; label: string; color: string }[] = []
            if (maLineVisibility.showLinearCloseTrend)
              items.push({ key: 'tlr', label: 'Trend LR', color: LINEAR_CLOSE_TREND_COLOR })
            if (maLineVisibility.showSma20)
              items.push({ key: 'sma', label: `SMA${MA_SMA_PERIOD}`, color: MA_LINE_COLORS.sma20 })
            if (maLineVisibility.showEma9)
              items.push({ key: 'e9', label: `EMA${MA_EMA_FAST_PERIOD}`, color: MA_LINE_COLORS.ema9 })
            if (maLineVisibility.showEma21)
              items.push({ key: 'e21', label: `EMA${MA_EMA_SLOW_PERIOD}`, color: MA_LINE_COLORS.ema21 })
            if (maLineVisibility.showCustomEma && customEmaPeriod != null && customEmaPeriod >= 2)
              items.push({
                key: 'ecust',
                label: `EMA${customEmaPeriod}`,
                color: MA_LINE_COLORS.emaCustom,
              })
            if (maLineVisibility.showSupportResistance) {
              items.push({ key: 'srs', label: `S${SR_SWING_PERIOD}`, color: SR_LINE_COLORS.support })
              items.push({ key: 'srr', label: `R${SR_SWING_PERIOD}`, color: SR_LINE_COLORS.resistance })
            }
            return items.map((item, i) => (
              <Fragment key={item.key}>
                {i > 0 ? <span className="text-muted"> · </span> : null}
                <span style={{ color: item.color }}>{item.label}</span>
              </Fragment>
            ))
          })()}
        </div>
      ) : null}
    </div>
  )
}
