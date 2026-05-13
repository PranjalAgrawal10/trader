import type { CSSProperties } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowDown, faArrowUp, faMinus } from '@fortawesome/free-solid-svg-icons'
import type { MlPredictionLogEntry } from '../utils/mlPredictionHistory'
import { formatMlTargetBarRibbon, sortMlRibbonEntries } from '../utils/mlPredictionHistory'

const RIBBON_TEXT_SHADOW =
  '0.5px 0 0 rgb(17 24 39 / 92%), -0.5px 0 0 rgb(17 24 39 / 92%), 0 0.5px 0 rgb(17 24 39 / 88%)'

const COL = {
  up: '#22c55e',
  down: '#dc2626',
  neutral: '#94a3b8',
} as const

function iconForDirection(d: MlPredictionLogEntry['direction']) {
  if (d === 'up') return faArrowUp
  if (d === 'down') return faArrowDown
  return faMinus
}

function colorForDirection(d: MlPredictionLogEntry['direction']) {
  if (d === 'up') return COL.up
  if (d === 'down') return COL.down
  return COL.neutral
}

type SvgRibbonProps = {
  entries: readonly MlPredictionLogEntry[]
  cx: number
  yTop: number
  iconPx?: number
}

/** Compact next-bar direction icons inside an SVG (<code>foreignObject</code>) for candlesticks and Recharts. */
export function MlDirectionRibbonSvg({ entries, cx, yTop, iconPx = 7 }: SvgRibbonProps) {
  const sorted = sortMlRibbonEntries(entries)
  if (sorted.length === 0) return null
  const title = formatMlTargetBarRibbon(entries) ?? ''
  const n = sorted.length
  const gapPx = Math.max(1, Math.round(iconPx * 0.2))
  const padPx = Math.max(2, Math.round(iconPx * 0.35))
  const fw = Math.min(260, padPx * 2 + n * iconPx + Math.max(0, n - 1) * gapPx + 2)
  const fh = Math.max(Math.round(iconPx + 5), Math.round(iconPx * 1.35 + 4))

  return (
    <g style={{ pointerEvents: 'none' }}>
      <title>{title}</title>
      <foreignObject x={cx - fw / 2} y={yTop} width={fw} height={fh}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: gapPx,
            height: `${fh}px`,
            padding: `0 ${padPx}px`,
            boxSizing: 'border-box',
            lineHeight: 1,
            textShadow: RIBBON_TEXT_SHADOW,
          }}
        >
          {sorted.map((e, i) => (
            <FontAwesomeIcon
              key={`${e.id}-${i}`}
              icon={iconForDirection(e.direction)}
              style={{
                color: colorForDirection(e.direction),
                fontSize: `${iconPx}px`,
                display: 'block',
                flexShrink: 0,
              }}
              aria-hidden
            />
          ))}
        </div>
      </foreignObject>
    </g>
  )
}

type HtmlRibbonProps = {
  entries: readonly MlPredictionLogEntry[]
  className?: string
  iconPx?: number
  style?: CSSProperties
}

/** Inline arrows for HTML tooltips / panels. */
export function MlDirectionRibbonHtml({ entries, className, iconPx = 9, style }: HtmlRibbonProps) {
  const sorted = sortMlRibbonEntries(entries)
  if (sorted.length === 0) return null
  const titleText = formatMlTargetBarRibbon(entries) ?? undefined
  const gap = Math.max(1, Math.round(iconPx * 0.22))

  return (
    <span
      className={className}
      title={titleText}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap,
        verticalAlign: 'middle',
        lineHeight: 1,
        ...style,
      }}
    >
      {sorted.map((e, i) => (
        <FontAwesomeIcon
          key={`${e.id}-${i}`}
          icon={iconForDirection(e.direction)}
          style={{
            color: colorForDirection(e.direction),
            fontSize: `${iconPx}px`,
            display: 'block',
            flexShrink: 0,
          }}
          aria-hidden
        />
      ))}
    </span>
  )
}
