import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { ChartPointOhlc } from '../utils/liveCandleMerge'

const PAD = { top: 6, right: 8, bottom: 22, left: 52 }

/** Bullish / bearish candle colors (typical trading terminal green / red). */
const CANDLE = {
  upFill: '#22c55e',
  upStroke: '#15803d',
  downFill: '#ef4444',
  downStroke: '#b91c1c',
  grid: 'rgba(173, 181, 189, 0.35)',
  text: '#adb5bd',
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

export function CandlestickChart({ data }: { data: ChartPointOhlc[] }) {
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

    return { plotW, plotH, min, max, yPrice, n, slotW, bodyW }
  }, [data, w, h])

  const yTicks = useMemo(() => {
    if (!layout) return []
    const { min, max } = layout
    const mid = (min + max) / 2
    return [max, mid, min].filter((v, i, a) => a.findIndex((x) => Math.abs(x - v) < 1e-9) === i)
  }, [layout])

  if (data.length === 0) return null

  return (
    <div ref={ref} className="w-100 h-100 position-relative">
      {layout ? (
        <svg width={w} height={h} className="d-block" role="img" aria-label="OHLC candlestick chart">
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
              <g key={`${c.t}-${c.idx}`}>
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
        </svg>
      ) : (
        <div className="d-flex align-items-center justify-content-center text-secondary small h-100">
          Resizing…
        </div>
      )}
    </div>
  )
}
