import { useEffect, useMemo, useState } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Card, Col, Row, Table } from 'react-bootstrap'
import { api } from '../api/client'
import { ChartWithRightGutter } from '../components/ChartWithRightGutter'
import { Layout } from '../components/Layout'
import { formatLocalDateTime } from '../utils/formatLocalDateTime'

function formatInr(amount: number): string {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    maximumFractionDigits: 0,
  }).format(amount)
}

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

  const pnlCharts = useMemo(() => {
    const sorted = [...rows].sort(
      (a, b) => new Date(a.executedAt).getTime() - new Date(b.executedAt).getTime(),
    )
    let cumulative = 0
    const cumulativeSeries = sorted.map((t, idx) => {
      const leg =
        t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)) ? Number(t.realizedPnl) : null
      if (leg != null) cumulative += leg
      return {
        seq: idx + 1,
        cumulativeRealizedPnl: cumulative,
        tradeRealizedPnl: leg,
        executedAt: t.executedAt,
        symbol: t.symbol,
      }
    })
    const perTradeBars = sorted
      .map((t, idx) => ({
        key: t.id,
        label: `${idx + 1}`,
        symbol: t.symbol,
        net: t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)) ? Number(t.realizedPnl) : null,
      }))
      .filter((x): x is typeof x & { net: number } => x.net != null)
    return { cumulativeSeries, perTradeBars }
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
                    <ResponsiveContainer width="100%" height="100%">
                    <LineChart data={pnlCharts.cumulativeSeries} margin={{ top: 6, right: 8, left: 4, bottom: 4 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#49505733" />
                      <XAxis dataKey="seq" stroke="#adb5bd" tick={{ fontSize: 10 }} />
                      <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} width={48} />
                      <Tooltip
                        formatter={(value: number) => formatInr(value)}
                        labelFormatter={(_, payload) => {
                          const p = payload?.[0]?.payload as { executedAt?: string; symbol?: string } | undefined
                          if (!p?.executedAt) return ''
                          return `${p.symbol ?? ''} · ${formatLocalDateTime(p.executedAt)}`
                        }}
                        contentStyle={{
                          background: '#212529',
                          border: '1px solid #495057',
                          borderRadius: 8,
                          fontSize: 12,
                        }}
                      />
                      <ReferenceLine y={0} stroke="#6c757d" strokeDasharray="4 4" />
                      <Line
                        type="monotone"
                        dataKey="cumulativeRealizedPnl"
                        stroke="#fd7e14"
                        strokeWidth={2}
                        dot={{ r: 2 }}
                      />
                    </LineChart>
                  </ResponsiveContainer>
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
                    <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={pnlCharts.perTradeBars} margin={{ top: 6, right: 8, left: 4, bottom: 4 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#49505733" />
                      <XAxis dataKey="label" stroke="#adb5bd" tick={{ fontSize: 10 }} />
                      <YAxis stroke="#adb5bd" tick={{ fontSize: 10 }} width={48} />
                      <Tooltip
                        formatter={(value: number) => formatInr(value)}
                        labelFormatter={(_, payload) => {
                          const p = payload?.[0]?.payload as { symbol?: string } | undefined
                          return p?.symbol ?? ''
                        }}
                        contentStyle={{
                          background: '#212529',
                          border: '1px solid #495057',
                          borderRadius: 8,
                          fontSize: 12,
                        }}
                      />
                      <ReferenceLine y={0} stroke="#6c757d" strokeDasharray="4 4" />
                      <Bar dataKey="net" maxBarSize={36} radius={[2, 2, 0, 0]}>
                        {pnlCharts.perTradeBars.map((r) => (
                          <Cell key={r.key} fill={r.net >= 0 ? '#198754' : '#dc3545'} />
                        ))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
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
