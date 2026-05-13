import {
  type CSSProperties,
  type PointerEvent as ReactPointerEvent,
  useCallback,
  useRef,
  useState,
} from 'react'
import { clampChartPanOffsetBars } from '../utils/chartZoom'

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

/**
 * Horizontal drag-to-pan when the series is zoomed to a window smaller than the full download.
 * Drag right → older bars; drag left → newer bars.
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
  const sessionRef = useRef<{ pointerStartX: number; panAtStart: number } | null>(null)

  const endDrag = useCallback((el: HTMLElement, pointerId: number) => {
    sessionRef.current = null
    setDragging(false)
    try {
      el.releasePointerCapture(pointerId)
    } catch {
      /* already released */
    }
  }, [])

  const onPointerDown = useCallback(
    (e: ReactPointerEvent<HTMLElement>) => {
      if (!enabled || e.button !== 0) return
      e.currentTarget.setPointerCapture(e.pointerId)
      sessionRef.current = { pointerStartX: e.clientX, panAtStart: panOffsetBars }
      setDragging(true)
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
      const deltaBars = Math.round(dx / pxPerBar)
      const next = clampChartPanOffsetBars(sessionRef.current.panAtStart + deltaBars, totalBars, visibleBarCount)
      setPanOffsetBars(next)
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
