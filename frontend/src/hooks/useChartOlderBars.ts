import { useCallback, useEffect, useRef, useState } from 'react'
import axios from 'axios'
import { fetchMergedHistoricalChartCandles } from '../api/kiteChartHistorical'
import type { ChartPointWithMa } from '../utils/movingAverages'
import {
  buildOlderWindowQuery,
  canFetchOlderThanWindow,
  prependOlderChartPoints,
} from '../utils/chartOlderBars'
import type { HistoricalChartCandlesResponse } from '../api/kiteChartHistorical'

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

type CandleWindow = { from: string; to: string }

export type ChartPointsFromMerged = (data: HistoricalChartCandlesResponse) => ChartPointWithMa[]

/**
 * Prepends older OHLC when the chart is panned near the left edge (see {@link InstrumentPriceChart}).
 */
export function useChartOlderBars(options: {
  instrumentToken: string | null | undefined
  interval: string
  candleWindow: CandleWindow | null | undefined
  series: ChartPointWithMa[]
  chartPointsFromMerged: ChartPointsFromMerged
  setSeries: React.Dispatch<React.SetStateAction<ChartPointWithMa[]>>
}): {
  loadOlderBars: () => Promise<void>
  loadingOlderBars: boolean
  olderFetchError: string | null
  canLoadOlderBars: boolean
} {
  const { instrumentToken, interval, candleWindow, series, chartPointsFromMerged, setSeries } = options
  const token = instrumentToken?.trim() ?? ''
  const [loadingOlderBars, setLoadingOlderBars] = useState(false)
  const [olderExhausted, setOlderExhausted] = useState(false)
  const [olderFetchError, setOlderFetchError] = useState<string | null>(null)
  const inFlightRef = useRef(false)

  useEffect(() => {
    setOlderExhausted(false)
    setOlderFetchError(null)
    inFlightRef.current = false
  }, [token, interval, candleWindow?.from, candleWindow?.to])

  const loadOlderBars = useCallback(async () => {
    const s = series
    if (!token || !candleWindow || s.length === 0 || olderExhausted || loadingOlderBars || inFlightRef.current)
      return
    const earliest = s[0].t
    if (!canFetchOlderThanWindow(earliest, candleWindow.from)) {
      setOlderExhausted(true)
      return
    }
    const q = buildOlderWindowQuery(earliest, candleWindow)
    if (!q) {
      setOlderExhausted(true)
      return
    }

    inFlightRef.current = true
    setLoadingOlderBars(true)
    setOlderFetchError(null)

    try {
      const data = await fetchMergedHistoricalChartCandles(token, interval, q)
      const pts = chartPointsFromMerged(data)
      if (pts.length === 0) {
        setOlderExhausted(true)
        return
      }
      setSeries((prev) => prependOlderChartPoints(prev, pts))
    } catch (e) {
      setOlderFetchError(problemDetail(e))
      setOlderExhausted(true)
    } finally {
      inFlightRef.current = false
      setLoadingOlderBars(false)
    }
  }, [
    token,
    interval,
    candleWindow,
    series,
    chartPointsFromMerged,
    setSeries,
    olderExhausted,
    loadingOlderBars,
  ])

  const canLoadOlderBars =
    Boolean(token && candleWindow?.from && candleWindow?.to) &&
    series.length > 0 &&
    !olderExhausted &&
    !loadingOlderBars

  return {
    loadOlderBars,
    loadingOlderBars,
    olderFetchError,
    canLoadOlderBars,
  }
}
