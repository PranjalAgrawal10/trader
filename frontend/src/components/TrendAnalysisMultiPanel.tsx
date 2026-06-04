import axios from 'axios'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, Button, Spinner, Table } from 'react-bootstrap'
import { fetchHistoricalChartOhlcMulti } from '../api/kiteChartHistorical'
import { summarizeCloseLinearTrend, type CloseLinearTrendSummary } from '../utils/closeLinearTrend'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'

const TREND_GROUP_FAST = new Set(['1m', '2m', '3m', '4m'])
const TREND_GROUP_CORE = new Set(['5m', '10m', '15m', '30m'])

function bucketTrendIntervals(intervals: readonly string[]): string[][] {
  const fast: string[] = []
  const core: string[] = []
  const rest: string[] = []
  for (const iv of intervals) {
    if (TREND_GROUP_FAST.has(iv)) fast.push(iv)
    else if (TREND_GROUP_CORE.has(iv)) core.push(iv)
    else rest.push(iv)
  }
  return [fast, core, rest].filter((g) => g.length > 0)
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const d = err.response?.data as { detail?: string; title?: string } | undefined
    if (d?.detail && typeof d.detail === 'string') return d.detail
    if (d?.title && typeof d.title === 'string') return d.title
    return err.message
  }
  return err instanceof Error ? err.message : 'Request failed.'
}

function classifyTrend(s: CloseLinearTrendSummary): 'up' | 'down' | 'neutral' {
  const scale = Math.max(Math.abs(s.firstClose), 1e-9)
  const delta = s.fitAtLast - s.fitAtFirst
  const rel = Math.abs(delta) / scale
  // ~minimum ~0.015% LR move vs first close treats near-flat as neutral
  if (rel < 0.00015) return 'neutral'
  return delta > 0 ? 'up' : 'down'
}

export type TrendAnalysisMultiPanelVariant = 'browseAlways' | 'favoriteLazy'

/** Multi-interval OHLC fetched with the parent’s Range (past window); least-squares on close matches chart “Trend LR” over the downloaded series (not zoom). */
export function TrendAnalysisMultiPanel({
  instrumentToken,
  symbolLabel,
  historicalQueryExtra,
  selectedIntervalsOrdered,
  variant,
}: {
  instrumentToken: string | null | undefined
  symbolLabel: string
  historicalQueryExtra: Record<string, string | undefined>
  selectedIntervalsOrdered: readonly string[]
  variant: TrendAnalysisMultiPanelVariant
}) {
  const shouldAutoRun = variant === 'browseAlways'
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  /** Stable for effect deps — parent often passes a new array instance each render with the same intervals. */
  const intervalsKey = selectedIntervalsOrdered.join('|')
  const extrasKey = JSON.stringify(historicalQueryExtra)
  const [rowsByInterval, setRowsByInterval] = useState<
    Record<
      string,
      | {
          bars: number
          windowDeltaPct: number
          lrSlopePerBar: number
          lrDir: 'up' | 'down' | 'neutral'
          fromIso: string
          toIso: string
        }
      | null
      | 'error'
    >
  >({})

  useEffect(() => {
    setRowsByInterval({})
    setError(null)
  }, [instrumentToken])

  const fetchAnalysis = useCallback(async () => {
    const intervalsList = intervalsKey.length > 0 ? intervalsKey.split('|') : []
    if (!instrumentToken || intervalsList.length === 0) return
    setLoading(true)
    setError(null)
    try {
      const grouped = bucketTrendIntervals(intervalsList)
      const results = await Promise.all(
        grouped.map(async (group) => {
          try {
            const data = await fetchHistoricalChartOhlcMulti(instrumentToken, group, historicalQueryExtra)
            return data.items.map((item) => {
              const closes = item.candles.map((c) => Number(c.close)).filter((x) => Number.isFinite(x))
              const summary = summarizeCloseLinearTrend(closes)
              if (!summary) return { interval: item.interval, payload: null as null }
              return {
                interval: item.interval,
                payload: {
                  bars: summary.barCount,
                  windowDeltaPct: summary.windowDeltaPct,
                  lrSlopePerBar: summary.slopePerBar,
                  lrDir: classifyTrend(summary),
                  fromIso: item.from,
                  toIso: item.to,
                },
              }
            })
          } catch (e: unknown) {
            if (axios.isAxiosError(e) && e.code === 'ERR_CANCELED') throw e
            return group.map((interval) => ({ interval, payload: 'error' as const }))
          }
        }),
      )

      const next: typeof rowsByInterval = {}
      for (const groupResult of results) {
        for (const r of groupResult) {
          if (r.payload === 'error') next[r.interval] = 'error'
          else next[r.interval] = r.payload ?? null
        }
      }
      setRowsByInterval(next)
    } catch (e: unknown) {
      if (axios.isAxiosError(e) && e.code === 'ERR_CANCELED') return
      setError(problemDetail(e))
    } finally {
      setLoading(false)
    }
  }, [instrumentToken, intervalsKey, extrasKey])

  useEffect(() => {
    if (!instrumentToken || intervalsKey.length === 0) return
    void fetchAnalysis()
  }, [instrumentToken, intervalsKey, extrasKey, fetchAnalysis])

  const majority = useMemo(() => {
    let up = 0
    let down = 0
    let neutral = 0
    let errs = 0
    let done = 0
    for (const iv of selectedIntervalsOrdered) {
      const row = rowsByInterval[iv]
      if (row === 'error') {
        errs++
        continue
      }
      if (row == null) continue
      done++
      if (row.lrDir === 'up') up++
      else if (row.lrDir === 'down') down++
      else neutral++
    }
    const totalVotes = up + down + neutral
    let label = '—'
    if (totalVotes > 0) {
      if (up >= down && up >= neutral) label = totalVotes === up ? `${up}/${totalVotes} up` : `Up leads (${up}/${totalVotes})`
      else if (down >= neutral) label = totalVotes === down ? `${down}/${totalVotes} down` : `Down leads (${down}/${totalVotes})`
      else label = `Sideways-heavy (${neutral}/${totalVotes})`
    }
    return { up, down, neutral, errs, totalVotes: selectedIntervalsOrdered.length, done, label }
  }, [rowsByInterval, selectedIntervalsOrdered])

  if (!instrumentToken) return null

  const body = selectedIntervalsOrdered.length === 0 ? (
    <p className="text-secondary small mb-0 py-2">Select at least one interval under Trend analysis.</p>
  ) : (
    <>
      {error ? (
        <Alert variant="danger" className="py-2 small mb-2">
          {error}
        </Alert>
      ) : null}
      {majority.done > 0 || majority.errs > 0 ? (
        <p className="small text-secondary mb-2">
          Consensus (LR slope on closes, same window as{' '}
          <strong>{variant === 'browseAlways' ? 'Range above' : 'grid Range'}</strong>):{' '}
          <strong className="text-body">{majority.label}</strong>
          {majority.errs > 0 ? (
            <>
              {' '}
              · <span className="text-warning">{majority.errs} failed fetch</span>
            </>
          ) : null}
        </p>
      ) : null}
      {loading ? (
        <div className="d-flex align-items-center gap-2 mb-2 text-secondary small" aria-live="polite">
          <Spinner animation="border" size="sm" role="status" />
          {Object.keys(rowsByInterval).length === 0
            ? 'Loading past data per interval…'
            : 'Refreshing multi-interval…'}
        </div>
      ) : null}
      <div className="table-responsive">
        <Table striped bordered hover size="sm" className="mb-0 small font-monospace">
          <thead className="table-light">
            <tr>
              <th>Interval</th>
              <th>Bars</th>
              <th title="Close last − first vs first close (%)">Window Δ%</th>
              <th title="Least-squares on close vs bar index">LR tilt</th>
              <th title="OHLC range from / to in Indian time (Asia/Kolkata)">
                Fit window (IST)
              </th>
            </tr>
          </thead>
          <tbody>
            {selectedIntervalsOrdered.map((iv) => {
              const r = rowsByInterval[iv]
              const dirClass =
                r != null && r !== 'error'
                  ? r.lrDir === 'up'
                    ? 'text-success'
                    : r.lrDir === 'down'
                      ? 'text-danger'
                      : 'text-secondary'
                  : ''
              return (
                <tr key={`trend-analysis-${instrumentToken}-${iv}`}>
                  <td className="fw-semibold">{iv}</td>
                  <td>
                    {loading && r === undefined ? (
                      <Spinner animation="border" size="sm" />
                    ) : r === 'error' ? (
                      <span className="text-warning">fail</span>
                    ) : r && typeof r !== 'string' ? (
                      r.bars
                    ) : (
                      '—'
                    )}
                  </td>
                  <td>
                    {r != null && r !== 'error' ? `${r.windowDeltaPct >= 0 ? '+' : ''}${r.windowDeltaPct.toFixed(2)}%` : '—'}
                  </td>
                  <td className={dirClass}>
                    {r != null && r !== 'error' ? `${r.lrDir.toUpperCase()} (${r.lrSlopePerBar >= 0 ? '+' : ''}${r.lrSlopePerBar.toPrecision(4)}/bar)` : '—'}
                  </td>
                  <td className="small text-muted" style={{ whiteSpace: 'nowrap' }}>
                    {r != null && typeof r !== 'string'
                      ? `${formatLocalDateTime(r.fromIso)} - ${formatLocalDateTime(r.toIso)}`
                      : '—'}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </Table>
      </div>
      {!shouldAutoRun ? (
        <div className="mt-2 d-flex gap-2">
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            disabled={loading}
            className="py-0 px-2"
            onClick={() => void fetchAnalysis()}
          >
            Refresh
          </Button>
        </div>
      ) : (
        <div className="mt-2">
          <Button
            type="button"
            variant="outline-secondary"
            size="sm"
            disabled={loading}
            className="py-0 px-2"
            onClick={() => void fetchAnalysis()}
          >
            Refresh multi-timeframe
          </Button>
        </div>
      )}
    </>
  )

  if (shouldAutoRun) {
    return (
      <section className="mt-3" aria-labelledby="trend-multi-heading-browse">
        <h4 id="trend-multi-heading-browse" className="h6 text-secondary mb-2">
          Multi-interval trend ({symbolLabel}) — past data
        </h4>
        <p className="small text-muted mb-2" style={{ fontSize: '0.78rem' }}>
          One row per checked timeframe uses the same <strong>Range</strong> query as the main chart (server OHLC series); LR tilt mirrors{' '}
          <strong>Trend LR</strong> over that full downloaded window (before zoom).
        </p>
        {body}
      </section>
    )
  }

  return (
    <section className="mt-2" aria-labelledby="trend-multi-heading-favorite">
      <h4 id="trend-multi-heading-favorite" className="h6 text-secondary mb-2">
        Multi-interval trend ({symbolLabel}) — past Range
      </h4>
      <p className="small text-muted mb-2" style={{ fontSize: '0.72rem' }}>
        Fetches OHLC once per checked timeframe using the favorites grid Range. LR tilt follows close regression over the returned bars.
      </p>
      {body}
    </section>
  )
}
