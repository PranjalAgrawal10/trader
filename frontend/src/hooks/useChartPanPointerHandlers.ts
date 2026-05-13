import {
  type CSSProperties,
  type PointerEvent as ReactPointerEvent,
  useCallback,
  useRef,
  useState,
} from 'react'
import { clampChartPanOffsetBars, maxChartPanOffsetBars } from '../utils/chartZoom'

/** Past clamped zoom pan range, scrub drag into elastic px (smooth “keep dragging” past newest/oldest bar). */
const RUBBER_STRENGTH = 0.55
const RUBBER_CORE_PX = 240
/** Continued pull adds √ tail so motion never hits a brick wall below a sane screen cap. */
const RUBBER_SOFT_CAP_PX = 960

const RELEASE_TRANSITION = 'transform 0.22s cubic-bezier(0.22, 1, 0.36, 1)'

export type UseChartPanPointerHandlersResult = {
  /** Spread onto the chart viewport element (under zoom controls). */
  panPointerProps: {
    onPointerDown: (e: ReactPointerEvent<HTMLElement>) => void
    onPointerMove: (e: ReactPointerEvent<HTMLElement>) => void
    onPointerUp: (e: ReactPointerEvent<HTMLElement>) => void
    onPointerCancel: (e: ReactPointerEvent<HTMLElement>) => void
    style: CSSProperties | undefined
  }
}

function rubberPxFromExcessBars(excessBars: number, pxPerBar: number): number {
  if (excessBars === 0 || !Number.isFinite(excessBars)) return 0
  const strained = excessBars * pxPerBar * RUBBER_STRENGTH
  const a = Math.abs(strained)
  let mag = Math.min(RUBBER_CORE_PX, a)
  if (a > RUBBER_CORE_PX) {
    mag = RUBBER_CORE_PX + Math.sqrt(a - RUBBER_CORE_PX) * 36
    mag = Math.min(RUBBER_SOFT_CAP_PX, mag)
  }
  return Math.sign(strained) * mag
}

/**
 * Horizontal drag-to-pan when the series is zoomed to a window smaller than the full download.
 * Drag right → older bars; drag left → newer bars.
 * Past the newest or oldest bar, scrubbing applies a rubber-band translate so drag never feels blocked.
 */
export function useChartPanPointerHandlers(options: {
  enabled: boolean
  totalBars: number
  visibleBarCount: number | null
  panOffsetBars: number
  setPanOffsetBars: (value: number) => void
}): UseChartPanPointerHandlersResult {
  const { enabled, totalBars, visibleBarCount, panOffsetBars, setPanOffsetBars } = options
  const [dragging, setDragging] = useState(false)
  const [rubberTranslateX, setRubberTranslateX] = useState(0)
  const sessionRef = useRef<{ pointerStartX: number; panAtStart: number } | null>(null)

  const endDrag = useCallback((el: HTMLElement, pointerId: number) => {
    sessionRef.current = null
    setDragging(false)
    setRubberTranslateX(0)
    try {
      el.releasePointerCapture(pointerId)
    } catch {
      /* already released */
    }
  }, [])

  const onPointerDown = useCallback(
    (e: React.PointerEvent<HTMLElement>) => {
      if (!enabled || e.button !== 0) return
      e.currentTarget.setPointerCapture(e.pointerId)
      sessionRef.current = { pointerStartX: e.clientX, panAtStart: panOffsetBars }
      setDragging(true)
      setRubberTranslateX(0)
    },
    [enabled, panOffsetBars],
  )

  const onPointerMove = useCallback(
    (e: ReactPointerEvent<HTMLElement>) => {
      if (!enabled || sessionRef.current == null) return
      const rect = e.currentTarget.getBoundingClientRect()
      const w = rect.width > 0 ? rect.width : 1
      const vis = visibleBarCount ?? totalBars
      const pxPerBar = w / Math.max(vis, 1)
      const dx = e.clientX - sessionRef.current.pointerStartX
      const rawPan = sessionRef.current.panAtStart + dx / pxPerBar
      const maxP = maxChartPanOffsetBars(totalBars, visibleBarCount)

      let excessBars = 0
      if (rawPan < 0) {
        excessBars = rawPan
      } else if (maxP >= 0 && rawPan > maxP) {
        excessBars = rawPan - maxP
      }

      const nextPan = clampChartPanOffsetBars(Math.round(rawPan), totalBars, visibleBarCount)
      setPanOffsetBars(nextPan)
      setRubberTranslateX(rubberPxFromExcessBars(excessBars, pxPerBar))
      e.preventDefault()
    },
    [enabled, totalBars, visibleBarCount, setPanOffsetBars],
  )

  const onPointerUp = useCallback(
    (e: ReactPointerEvent<HTMLElement>) => {
      if (sessionRef.current == null) return
      endDrag(e.currentTarget, e.pointerId)
    },
    [endDrag],
  )

  const onPointerCancel = useCallback(
    (e: ReactPointerEvent<HTMLElement>) => {
      if (sessionRef.current == null) return
      endDrag(e.currentTarget, e.pointerId)
    },
    [endDrag],
  )

  const style: CSSProperties | undefined = enabled
    ? {
        touchAction: 'none',
        cursor: dragging ? 'grabbing' : 'grab',
        userSelect: 'none',
        transform: `translateX(${rubberTranslateX}px)`,
        transition: dragging ? undefined : RELEASE_TRANSITION,
        willChange: dragging ? 'transform' : undefined,
      }
    : undefined

  return {
    panPointerProps: {
      onPointerDown,
      onPointerMove,
      onPointerUp,
      onPointerCancel,
      style,
    },
  }
}
