import { useEffect, useState } from 'react'
import { createMarketHubConnection, startMarketHub, type MarketTickBatchItem } from '../services/marketHub'

export interface UseLiveMarketTickResult {
  lastPrice: number | null
  lastTick: MarketTickBatchItem | null
}

/** Subscribes to Kite LTP via SignalR for one instrument token. */
export function useLiveMarketTick(instrumentToken: string | null, enabled: boolean): UseLiveMarketTickResult {
  const [lastPrice, setLastPrice] = useState<number | null>(null)
  const [lastTick, setLastTick] = useState<MarketTickBatchItem | null>(null)

  useEffect(() => {
    if (!enabled || !instrumentToken?.trim()) {
      setLastPrice(null)
      setLastTick(null)
      return
    }

    const token = instrumentToken.trim()
    let conn = createMarketHubConnection()

    const onTicks = (batch: MarketTickBatchItem[]) => {
      const want = Number(token)
      if (!Number.isFinite(want)) return
      for (let i = batch.length - 1; i >= 0; i--) {
        if (batch[i].i === want) {
          setLastPrice(batch[i].p)
          setLastTick(batch[i])
          return
        }
      }
    }

    let cancelled = false
    ;(async () => {
      try {
        conn = await startMarketHub(conn)
        conn.on('ticks', onTicks)
        if (cancelled) return
        await conn.invoke('SubscribeInstrument', token)
      } catch {
        if (!cancelled) {
          setLastPrice(null)
          setLastTick(null)
        }
      }
    })()

    return () => {
      cancelled = true
      conn.off('ticks', onTicks)
      setLastTick(null)
      ;(async () => {
        try {
          if (conn.state === 'Connected') await conn.invoke('UnsubscribeInstrument', token)
        } catch {
          /* ignore */
        }
        try {
          await conn.stop()
        } catch {
          /* ignore */
        }
      })()
    }
  }, [enabled, instrumentToken])

  return { lastPrice, lastTick }
}
