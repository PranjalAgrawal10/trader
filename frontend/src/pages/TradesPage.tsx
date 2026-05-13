import { useEffect, useMemo, useState } from 'react'
import { Card, Col, Row, Table } from 'react-bootstrap'
import { api } from '../api/client'
import { ChartWithRightGutter } from '../components/ChartWithRightGutter'
import type { LwSyntheticBarRow } from '../components/LwMiscCharts'
import { LwSyntheticHistogram, LwTimeLine } from '../components/LwMiscCharts'
import { Layout } from '../components/Layout'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'

interface TradeRow {
  id: string
  botId: string
  symbol: string
  side: number | string
  quantity: number
  price: number
  realizedPnl: number | null
  executedAt: string
}

export function TradesPage() {
  const [rows, setRows] = useState<TradeRow[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api
      .get<TradeRow[]>('/trades')
      .then((r) => setRows(r.data))
      .catch(() => setError('Failed to load trades.'))
  }, [])

  const lwTradeCharts = useMemo(() => {
    const sorted = [...rows].sort(
      (a, b) => new Date(a.executedAt).getTime() - new Date(b.executedAt).getTime(),
    )
    let cumulative = 0
    let lastTs = 0
    const cumulativePoints: { timeMs: number; value: number }[] = []
    for (const t of sorted) {
      const base = new Date(t.executedAt).getTime()
      const timeMs = Math.max(lastTs + 1, base)
      lastTs = timeMs
      const leg =
        t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)) ? Number(t.realizedPnl) : null
      if (leg != null) cumulative += leg
      cumulativePoints.push({ timeMs, value: cumulative })
    }
    const perTradeBars: LwSyntheticBarRow[] = sorted
      .map((t, idx) => ({
        key: t.id,
        label: `${idx + 1}`,
        net: t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)) ? Number(t.realizedPnl) : null,
      }))
      .filter((x): x is typeof x & { net: number } => x.net != null)
      .map((r) => ({
        key: r.key,
        label: r.label,
        value: r.net,
        color: r.net >= 0 ? '#198754' : '#dc3545',
      }))
    return { cumulativePoints, perTradeBars }
  }, [rows])

  const hasRealizedPnl = rows.some(
    (t) => t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)),
  )

  const sideLabel = (s: number | string) => {
    const v = typeof s === 'string' ? s.toLowerCase() : s
    if (v === 'sell' || v === 1) return 'Sell'
    return 'Buy'
  }

  return (
    <Layout>
      <h1 className="h3 mb-1">Trade history</h1>
      <p className="text-secondary small mb-4">Filtered server-side to the latest window.</p>

      {error ? <p className="text-danger small mb-4">{error}</p> : null}

      {hasRealizedPnl ? (
        <Row className="g-3 mb-4">
          <Col xs={12} lg={6}>
            <Card className="border-secondary h-100">
              <Card.Body className="pb-2">
                <Card.Subtitle className="text-secondary small mb-2">Cumulative realized P&amp;L</Card.Subtitle>
                <div style={{ height: '14rem' }}>
                  <ChartWithRightGutter>
                    <LwTimeLine points={lwTradeCharts.cumulativePoints} heightPx={224} zeroBaseline />
                  </ChartWithRightGutter>
                </div>
              </Card.Body>
            </Card>
          </Col>
          <Col xs={12} lg={6}>
            <Card className="border-secondary h-100">
              <Card.Body className="pb-2">
                <Card.Subtitle className="text-secondary small mb-2">
                  Realized P&amp;L per fill (sequence order)
                </Card.Subtitle>
                <div style={{ height: '14rem' }}>
                  <ChartWithRightGutter>
                    <LwSyntheticHistogram rows={lwTradeCharts.perTradeBars} heightPx={224} />
                  </ChartWithRightGutter>
                </div>
              </Card.Body>
            </Card>
          </Col>
        </Row>
      ) : rows.length > 0 ? (
        <p className="text-muted small mb-4">
          Charts appear here when trades include realized P&amp;L values (currently showing fills only).
        </p>
      ) : null}

      <div className="rounded border border-secondary overflow-x-auto">
        <Table responsive hover className="mb-0 small">
          <thead className="table-dark">
            <tr>
              <th>Time</th>
              <th>Symbol</th>
              <th>Side</th>
              <th>Qty</th>
              <th>Price</th>
              <th>P&L</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((t) => (
              <tr key={t.id}>
                <td className="text-secondary text-nowrap">
                  {formatLocalDateTime(t.executedAt)}
                </td>
                <td className="fw-medium">{t.symbol}</td>
                <td>{sideLabel(t.side)}</td>
                <td>{t.quantity}</td>
                <td>{t.price}</td>
                <td>{t.realizedPnl ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      </div>
    </Layout>
  )
}
