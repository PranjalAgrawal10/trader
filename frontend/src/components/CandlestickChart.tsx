import { Fragment, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { ChartPointWithMa, MaLineVisibility } from '../utils/movingAverages'
import {
  DEFAULT_MA_LINE_VISIBILITY,
  MA_EMA_FAST_PERIOD,
  MA_EMA_SLOW_PERIOD,
  MA_LINE_COLORS,
  MA_SMA_PERIOD,
} from '../utils/movingAverages'

const PAD = { top: 6, right: 8, bottom: 34, left: 52 }

/** Bullish / bearish candle colors (typical trading terminal green / red). */
const CANDLE = {
  upFill: '#22c55e',
  upStroke: '#15803d',
  downFill: '#ef4444',
  downStroke: '#b91c1c',
  grid: 'rgba(173, 181, 189, 0.35)',
  text: '#adb5bd',
}

type MaKey = keyof Pick<ChartPointWithMa, 'sma20' | 'ema9' | 'ema21'>

function buildMaPath(key: MaKey, data: ChartPointWithMa[], slotW: number, yPrice: (p: number) => number): string {
  let d = ''
  let penUp = true
  for (let i = 0; i < data.length; i++) {
    const v = data[i][key]
    if (v == null || !Number.isFinite(v)) {
      penUp = true
      continue
    }
    const cx = PAD.left + i * slotW + slotW / 2
    const cy = yPrice(v)
    d += penUp ? `M ${cx} ${cy}` : ` L ${cx} ${cy}`
    penUp = false
  }
  return d
}

/** X-axis time labels: a few ticks across the window; `shortTime` omits date when range is one calendar day. */
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

function sameUtcDay(a: Date, b: Date): boolean {
  return (
    a.getUTCFullYear() === b.getUTCFullYear() &&
    a.getUTCMonth() === b.getUTCMonth() &&
    a.getUTCDate() === b.getUTCDate()
  )
}

function isIntradayRange(data: ChartPointWithMa[]): boolean {
  if (data.length < 2) return true
  const a = new Date(data[0].t)
  const b = new Date(data[data.length - 1].t)
  return sameUtcDay(a, b)
}

function formatAxisTimeLabel(iso: string, shortTime: boolean): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  if (shortTime) {
    return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
  }
  return d.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
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
}: {
  data: ChartPointWithMa[]
  maLineVisibility?: MaLineVisibility
}) {
  const { ref, w, h } = useContainerPixelSize<HTMLDivElement>()

  const layout = useMemo(() => {
    if (data.length === 0 || w < 40 || h < 40) return null

    const plotW = w - PAD.left - PAD.right
    const plotH = h - PAD.top - PAD.bottom
    if (plotW < 10 || plotH < 10) return null

    let min = Infinity
    let max = -Infinity
    for (const c of data) {
      min = Math.min(min, c.low)
      max = Math.max(max, c.high)
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
    }
    if (!Number.isFinite(min) || !Number.isFinite(max)) return null
    if (min === max) {
      const pad = min === 0 ? 1 : Math.abs(min) * 0.001
      min -= pad
      max += pad
    }

    const yPrice = (p: number) => PAD.top + ((max - p) / (max - min)) * plotH
    const n = data.length
    const slotW = plotW / n
    const bodyW = Math.min(Math.max(1, slotW * 0.65), 14)

    const pathSma = buildMaPath('sma20', data, slotW, yPrice)
    const pathEma9 = buildMaPath('ema9', data, slotW, yPrice)
    const pathEma21 = buildMaPath('ema21', data, slotW, yPrice)

    const shortDay = isIntradayRange(data)
    const targetXTicks = Math.min(6, Math.max(2, Math.floor(plotW / 72)))
    const xTickIndices = computeXTickIndices(n, targetXTicks)
    const xTicks = xTickIndices.map((i) => ({
      i,
      cx: PAD.left + i * slotW + slotW / 2,
      label: formatAxisTimeLabel(data[i].t, shortDay),
    }))

    const plotBottomY = PAD.top + plotH

    return {
      plotW,
      plotH,
      min,
      max,
      yPrice,
      n,
      slotW,
      bodyW,
      pathSma,
      pathEma9,
      pathEma21,
      xTicks,
      plotBottomY,
    }
  }, [data, w, h, maLineVisibility])

  const yTicks = useMemo(() => {
    if (!layout) return []
    const { min, max } = layout
    const mid = (min + max) / 2
    return [max, mid, min].filter((v, i, a) => a.findIndex((x) => Math.abs(x - v) < 1e-9) === i)
  }, [layout])

  const [hover, setHover] = useState<{ idx: number; tipX: number; tipY: number } | null>(null)

  if (data.length === 0) return null

  return (
    <div ref={ref} className="w-100 h-100 position-relative">
      {layout ? (
        <svg width={w} height={h} className="d-block" role="img" aria-label="OHLC candlestick chart with MA and EMA">
          {yTicks.map((tp) => (
            <g key={tp}>
              <line
                x1={PAD.left}
                x2={w - PAD.right}
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

          <line
            x1={PAD.left}
            x2={w - PAD.right}
            y1={layout.plotBottomY}
            y2={layout.plotBottomY}
            stroke={CANDLE.grid}
            strokeWidth={1}
          />
          {layout.xTicks.map((xt) => (
            <text
              key={`xt-${xt.i}-${xt.label}`}
              x={xt.cx}
              y={layout.plotBottomY + 14}
              fill={CANDLE.text}
              fontSize={9}
              textAnchor="middle"
              style={{ userSelect: 'none' }}
            >
              {xt.label}
            </text>
          ))}

          {data.map((c, i) => {
            const cx = PAD.left + i * layout.slotW + layout.slotW / 2
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

          {layout.pathEma21 && maLineVisibility.showEma21 ? (
            <path
              d={layout.pathEma21}
              fill="none"
              stroke={MA_LINE_COLORS.ema21}
              strokeWidth={1.5}
              strokeLinecap="round"
              strokeLinejoin="round"
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
          <rect
            x={PAD.left}
            y={PAD.top}
            width={layout.plotW}
            height={layout.plotH}
            fill="transparent"
            style={{ cursor: 'crosshair' }}
            onMouseMove={(e) => {
              const svg = e.currentTarget.ownerSVGElement
              if (!svg || !ref.current) return
              const svgR = svg.getBoundingClientRect()
              const sx = ((e.clientX - svgR.left) / Math.max(svgR.width, 1)) * w
              const rel = sx - PAD.left
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
      {layout ? (
        <div
          className="position-absolute small text-secondary"
          style={{ right: 8, bottom: 2, fontSize: '0.65rem', pointerEvents: 'none' }}
        >
          {(() => {
            const items: { key: string; label: string; color: string }[] = []
            if (maLineVisibility.showSma20)
              items.push({ key: 'sma', label: `SMA${MA_SMA_PERIOD}`, color: MA_LINE_COLORS.sma20 })
            if (maLineVisibility.showEma9)
              items.push({ key: 'e9', label: `EMA${MA_EMA_FAST_PERIOD}`, color: MA_LINE_COLORS.ema9 })
            if (maLineVisibility.showEma21)
              items.push({ key: 'e21', label: `EMA${MA_EMA_SLOW_PERIOD}`, color: MA_LINE_COLORS.ema21 })
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
