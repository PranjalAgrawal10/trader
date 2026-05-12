import type { ReactNode } from 'react'
import { CHART_RIGHT_EDGE_GAP_FRACT } from '../constants/chartLayout'

/** Reserves {@link CHART_RIGHT_EDGE_GAP_FRACT} of width on the right; chart fills the remainder. */
export function ChartWithRightGutter({
  children,
  className = 'd-flex w-100 h-100 align-items-stretch',
}: {
  children: ReactNode
  className?: string
}) {
  const leftPct = (1 - CHART_RIGHT_EDGE_GAP_FRACT) * 100
  const rightPct = CHART_RIGHT_EDGE_GAP_FRACT * 100
  return (
    <div className={className} style={{ minHeight: 0 }}>
      <div className="h-100" style={{ width: `${leftPct}%`, minWidth: 0, minHeight: 0 }}>
        {children}
      </div>
      <div className="flex-shrink-0 h-100" style={{ width: `${rightPct}%`, minWidth: '2px' }} aria-hidden />
    </div>
  )
}
