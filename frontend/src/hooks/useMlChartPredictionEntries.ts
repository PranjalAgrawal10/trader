import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api/client'
import type { ChartPointWithMa } from '../utils/movingAverages'
import {
  historiesEqual,
  historyItemsFromApi,
  loadMlHistory,
  resolveMlHistory,
  saveMlHistory,
  type MlPredictionLogEntry,
  type MlPriceDirectionHistoryApiRow,
} from '../utils/mlPredictionHistory'

function maybeNotifyServerOfResolvedRow(prev: MlPredictionLogEntry[], resolved: MlPredictionLogEntry[]): void {
  for (const r of resolved) {
    const p = prev.find((x) => x.id === r.id)
    if (
      p &&
      p.outcome === 'pending' &&
      r.outcome !== 'pending' &&
      r.serverBacked &&
      r.nextBarTime &&
      r.nextClose != null
    ) {
      void api
        .patch(`/predictions/price-direction/${r.id}/resolve`, {
          nextBarTime: r.nextBarTime,
          nextClose: r.nextClose,
        })
        .catch(() => {})
    }
  }
}

/**
 * Loads classic + LightGBM price-direction history for a symbol/interval, resolves pending rows against the
 * visible candle series (same behavior as the ML next-bar overlays on `/instruments`).
 */
export function useMlChartPredictionEntries(
  instrumentToken: string | null | undefined,
  interval: string | null | undefined,
  candleSeries: readonly ChartPointWithMa[],
): {
  entries: readonly MlPredictionLogEntry[]
  reloadHistory: () => Promise<void>
} {
  const [history, setHistory] = useState<MlPredictionLogEntry[]>([])
  const [lightGbmHistory, setLightGbmHistory] = useState<MlPredictionLogEntry[]>([])
  const historySourceRef = useRef<'api' | 'local'>('api')

  const token = instrumentToken?.trim() ?? ''
  const iv = interval?.trim() ?? ''

  const reloadHistory = useCallback(async () => {
    if (!token || !iv) {
      setHistory([])
      setLightGbmHistory([])
      return
    }
    const [classicRes, lgbmRes] = await Promise.allSettled([
      api.get<MlPriceDirectionHistoryApiRow[]>('/predictions/price-direction/history', {
        params: { instrumentToken: token, interval: iv, take: 2000 },
      }),
      api.get<MlPriceDirectionHistoryApiRow[]>('/predictions/price-direction/lightgbm-triple-barrier/history', {
        params: { instrumentToken: token, interval: iv, take: 2000 },
      }),
    ])

    if (classicRes.status === 'fulfilled') {
      historySourceRef.current = 'api'
      setHistory(historyItemsFromApi(classicRes.value.data))
    } else {
      historySourceRef.current = 'local'
      setHistory(loadMlHistory(token, iv))
    }

    if (lgbmRes.status === 'fulfilled') {
      setLightGbmHistory(historyItemsFromApi(lgbmRes.value.data))
    } else {
      setLightGbmHistory([])
    }
  }, [token, iv])

  useEffect(() => {
    void reloadHistory()
  }, [reloadHistory])

  useEffect(() => {
    if (!token || !iv || candleSeries.length === 0) return
    setHistory((prev) => {
      const resolved = resolveMlHistory(prev, candleSeries)
      maybeNotifyServerOfResolvedRow(prev, resolved)
      if (historiesEqual(prev, resolved)) return prev
      if (historySourceRef.current === 'local') {
        saveMlHistory(token, iv, resolved)
      }
      return resolved
    })
  }, [token, iv, candleSeries])

  useEffect(() => {
    if (!token || !iv || candleSeries.length === 0) return
    setLightGbmHistory((prev) => {
      const resolved = resolveMlHistory(prev, candleSeries)
      maybeNotifyServerOfResolvedRow(prev, resolved)
      if (historiesEqual(prev, resolved)) return prev
      return resolved
    })
  }, [token, iv, candleSeries])

  const entries = useMemo(() => [...history, ...lightGbmHistory], [history, lightGbmHistory])

  return { entries, reloadHistory }
}
