import { useCallback, useEffect, useRef, useState } from 'react'

/** Toggle browser fullscreen on a chart panel (zoom + plot). */
export function useChartFullscreen() {
  const panelRef = useRef<HTMLDivElement | null>(null)
  const [fullscreenActive, setFullscreenActive] = useState(false)

  useEffect(() => {
    const sync = () => {
      setFullscreenActive(document.fullscreenElement === panelRef.current)
    }
    document.addEventListener('fullscreenchange', sync)
    return () => document.removeEventListener('fullscreenchange', sync)
  }, [])

  const toggleFullscreen = useCallback(async () => {
    const el = panelRef.current
    if (!el) return
    try {
      if (document.fullscreenElement === el) {
        await document.exitFullscreen()
      } else {
        await el.requestFullscreen()
      }
    } catch {
      // Unsupported, denied, or not a user gesture
    }
  }, [])

  return { panelRef, fullscreenActive, toggleFullscreen }
}
