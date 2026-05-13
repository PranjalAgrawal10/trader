import { Fragment, type CSSProperties } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowDown, faArrowUp, faMinus } from '@fortawesome/free-solid-svg-icons'
import type { MlPredictionLogEntry } from '../utils/mlPredictionHistory'
import { formatMlTargetBarRibbon, sortMlRibbonEntries } from '../utils/mlPredictionHistory'

const RIBBON_TEXT_SHADOW =
  '1px 0 0 rgb(33 37 41 / 90%), -1px 0 0 rgb(33 37 41 / 90%), 0 1px 0 rgb(33 37 41 / 90%)'

const COL = {
  up: '#22c55e',
  down: '#dc2626',
  neutral: '#94a3b8',
  paren: '#94a3b8',
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

/** Parenthesized next-bar directions inside an SVG (<code>foreignObject</code>) for candlesticks and Recharts. */
export function MlDirectionRibbonSvg({ entries, cx, yTop, iconPx = 10 }: SvgRibbonProps) {
  const sorted = sortMlRibbonEntries(entries)
  if (sorted.length === 0) return null
  const title = formatMlTargetBarRibbon(entries) ?? ''
  const n = sorted.length
  const gapPx = 3
  const fw = Math.min(
    320,
    Math.max(24, 8 + n * (iconPx + gapPx + 2) + Math.max(0, n - 1) * 5),
  )
  const fh = Math.round(iconPx + 6)

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
            fontSize: `${Math.max(8, iconPx - 2)}px`,
            color: COL.paren,
            lineHeight: 1,
            textShadow: RIBBON_TEXT_SHADOW,
            fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Monaco, monospace',
          }}
        >
          <span aria-hidden>(</span>
          {sorted.map((e, i) => (
            <Fragment key={`${e.id}-${i}`}>
              {i > 0 ? <span aria-hidden>,</span> : null}
              <FontAwesomeIcon
                icon={iconForDirection(e.direction)}
                style={{
                  color: colorForDirection(e.direction),
                  fontSize: `${iconPx}px`,
                  verticalAlign: 'middle',
                }}
                aria-hidden
              />
            </Fragment>
          ))}
          <span aria-hidden>)</span>
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

/** Parenthesized arrows for HTML tooltips / panels. */
export function MlDirectionRibbonHtml({ entries, className, iconPx = 12, style }: HtmlRibbonProps) {
  const sorted = sortMlRibbonEntries(entries)
  if (sorted.length === 0) return null
  const titleText = formatMlTargetBarRibbon(entries) ?? undefined

  return (
    <span
      className={className}
      title={titleText}
      style={{ display: 'inline-flex', alignItems: 'center', gap: 4, ...style }}
    >
      <span className="text-secondary" aria-hidden>
        (
      </span>
      {sorted.map((e, i) => (
        <Fragment key={`${e.id}-${i}`}>
          {i > 0 ? (
            <span className="text-secondary" aria-hidden>
              ,
            </span>
          ) : null}
          <FontAwesomeIcon
            icon={iconForDirection(e.direction)}
            style={{ color: colorForDirection(e.direction), fontSize: `${iconPx}px` }}
            aria-hidden
          />
        </Fragment>
      ))}
      <span className="text-secondary" aria-hidden>
        )
      </span>
    </span>
  )
}
