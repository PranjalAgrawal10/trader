import axios from 'axios'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Accordion, Alert, Button, Spinner, Table } from 'react-bootstrap'
import { api } from '../api/client'
import { summarizeCloseLinearTrend, type CloseLinearTrendSummary } from '../utils/closeLinearTrend'

interface HistoricalCandlesResponse {
  candles: { close: number }[]
  interval: string
  from: string
  to: string
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
  const [accordionKey, setAccordionKey] = useState<string | undefined>(undefined)
  const openFavorite = accordionKey === '0'
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
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

  const active = shouldAutoRun || openFavorite
  useEffect(() => {
    setRowsByInterval({})
    setError(null)
  }, [instrumentToken])

  const fetchAnalysis = useCallback(async () => {
    if (!instrumentToken || selectedIntervalsOrdered.length === 0) return
    setLoading(true)
    setError(null)
    try {
      const results = await Promise.all(
        selectedIntervalsOrdered.map(async (interval) => {
          try {
            const { data } = await api.get<HistoricalCandlesResponse>('/broker/kite/chart/historical-ohlc', {
              params: {
                instrumentToken,
                interval,
                ...historicalQueryExtra,
              },
            })
            const closes = data.candles.map((c) => Number(c.close)).filter((x) => Number.isFinite(x))
            const summary = summarizeCloseLinearTrend(closes)
            if (!summary) {
              return { interval, payload: null as null }
            }
            return {
              interval,
              payload: {
                bars: summary.barCount,
                windowDeltaPct: summary.windowDeltaPct,
                lrSlopePerBar: summary.slopePerBar,
                lrDir: classifyTrend(summary),
                fromIso: data.from,
                toIso: data.to,
              },
            }
          } catch (e: unknown) {
            if (axios.isAxiosError(e) && e.code === 'ERR_CANCELED') throw e
            return { interval, payload: 'error' as 'error' }
          }
        }),
      )

      const next: typeof rowsByInterval = {}
      for (const r of results) {
        if (r.payload === 'error') next[r.interval] = 'error'
        else next[r.interval] = r.payload ?? null
      }
      setRowsByInterval(next)
    } catch (e: unknown) {
      if (axios.isAxiosError(e) && e.code === 'ERR_CANCELED') return
      setError(problemDetail(e))
    } finally {
      setLoading(false)
    }
  }, [instrumentToken, selectedIntervalsOrdered, JSON.stringify(historicalQueryExtra)])

  useEffect(() => {
    if (!instrumentToken || !active || selectedIntervalsOrdered.length === 0) return
    void fetchAnalysis()
  }, [instrumentToken, active, selectedIntervalsOrdered.join(','), JSON.stringify(historicalQueryExtra), fetchAnalysis])

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
      {!shouldAutoRun && openFavorite ? null : loading && Object.keys(rowsByInterval).length === 0 ? (
        <div className="d-flex align-items-center gap-2 py-3 text-secondary small">
          <Spinner animation="border" size="sm" role="status" />
          Loading past data per interval…
        </div>
      ) : (
        <div className="table-responsive">
          <Table striped bordered hover size="sm" className="mb-0 small font-monospace">
            <thead className="table-light">
              <tr>
                <th>Interval</th>
                <th>Bars</th>
                <th title="Close last − first vs first close (%)">Window Δ%</th>
                <th title="Least-squares on close vs bar index">LR tilt</th>
                <th>Fit window (UTC)</th>
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
                      {r != null && typeof r !== 'string' ? `${formatIsoShort(r.fromIso)}→${formatIsoShort(r.toIso)}` : '—'}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </Table>
        </div>
      )}
      {!shouldAutoRun && openFavorite ? (
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
      ) : shouldAutoRun ? (
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
      ) : null}
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
    <Accordion
      className="mt-2"
      flush
      activeKey={accordionKey}
      onSelect={(eventKey) => {
        const ek = Array.isArray(eventKey) ? eventKey[0] : eventKey
        setAccordionKey(
          ek == null || ek === ''
            ? undefined
            : typeof ek === 'string'
              ? ek
              : String(ek),
        )
      }}
    >
      <Accordion.Item eventKey="0">
        <Accordion.Header>
          <span className="small fw-semibold">Multi-interval trend ({symbolLabel}) · past Range</span>
        </Accordion.Header>
        <Accordion.Body className="pt-2 pb-2">
          <p className="small text-muted mb-2" style={{ fontSize: '0.72rem' }}>
            Fetches OHLC once per checked timeframe using the favorites grid Range. LR tilt follows close regression over the returned bars.
          </p>
          {body}
        </Accordion.Body>
      </Accordion.Item>
    </Accordion>
  )
}

function formatIsoShort(iso: string): string {
  try {
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return iso.slice(0, 16)
    return `${d.getUTCFullYear()}-${pad2(d.getUTCMonth() + 1)}-${pad2(d.getUTCDate())} ${pad2(d.getUTCHours())}:${pad2(d.getUTCMinutes())}`
  } catch {
    return iso.slice(0, 16)
  }
}

function pad2(n: number): string {
  return String(n).padStart(2, '0')
}
