import { useEffect, useState } from 'react'
import { createMarketHubConnection, startMarketHub, type MarketTickBatchItem } from '../services/marketHub'

/** Subscribes to Kite LTP via SignalR for one instrument token. */
export function useLiveMarketTick(instrumentToken: string | null, enabled: boolean) {
  const [lastPrice, setLastPrice] = useState<number | null>(null)

  useEffect(() => {
    if (!enabled || !instrumentToken?.trim()) {
      setLastPrice(null)
      return
    }

    const token = instrumentToken.trim()
    const conn = createMarketHubConnection()

    const onTicks = (batch: MarketTickBatchItem[]) => {
      const want = Number(token)
      if (!Number.isFinite(want)) return
      for (let i = batch.length - 1; i >= 0; i--) {
        if (batch[i].i === want) {
          setLastPrice(batch[i].p)
          return
        }
      }
    }

    conn.on('ticks', onTicks)

    let cancelled = false
    ;(async () => {
      try {
        await startMarketHub(conn)
        if (cancelled) return
        await conn.invoke('SubscribeInstrument', token)
      } catch {
        if (!cancelled) setLastPrice(null)
      }
    })()

    return () => {
      cancelled = true
      conn.off('ticks', onTicks)
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

  return lastPrice
}
