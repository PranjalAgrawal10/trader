import { useEffect, useMemo, useState } from 'react'
import { Card, Col, Form, Row, Table } from 'react-bootstrap'
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

interface OrderRow {
  id: string
  botId: string
  externalId: string
  status: string
  createdAt: string
}

export function TradesPage() {
  const [rows, setRows] = useState<TradeRow[]>([])
  const [orders, setOrders] = useState<OrderRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [ordersError, setOrdersError] = useState<string | null>(null)
  const [orderSearch, setOrderSearch] = useState('')
  const [orderStatus, setOrderStatus] = useState('all')
  const [orderBotId, setOrderBotId] = useState('all')
  const [orderFrom, setOrderFrom] = useState('')
  const [orderTo, setOrderTo] = useState('')

  useEffect(() => {
    api
      .get<TradeRow[]>('/trades')
      .then((r) => setRows(r.data))
      .catch(() => setError('Failed to load trades.'))

    api
      .get<OrderRow[]>('/trades/orders')
      .then((r) => setOrders(r.data))
      .catch(() => setOrdersError('Failed to load orders.'))
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

  const orderStatusOptions = useMemo(
    () => [...new Set(orders.map((o) => o.status.trim()).filter((s) => s.length > 0))].sort((a, b) => a.localeCompare(b)),
    [orders],
  )

  const orderBotOptions = useMemo(
    () => [...new Set(orders.map((o) => o.botId.trim()).filter((s) => s.length > 0))].sort((a, b) => a.localeCompare(b)),
    [orders],
  )

  const filteredOrders = useMemo(() => {
    const q = orderSearch.trim().toLowerCase()
    const fromMs = orderFrom ? new Date(orderFrom).getTime() : null
    const toMs = orderTo ? new Date(orderTo).getTime() : null
    return orders.filter((o) => {
      if (orderStatus !== 'all' && o.status !== orderStatus) return false
      if (orderBotId !== 'all' && o.botId !== orderBotId) return false
      if (q.length > 0) {
        const hay = `${o.externalId} ${o.status} ${o.botId}`.toLowerCase()
        if (!hay.includes(q)) return false
      }
      const createdMs = new Date(o.createdAt).getTime()
      if (fromMs != null && Number.isFinite(fromMs) && createdMs < fromMs) return false
      if (toMs != null && Number.isFinite(toMs) && createdMs > toMs) return false
      return true
    })
  }, [orderBotId, orderFrom, orderSearch, orderStatus, orderTo, orders])

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

      <h2 className="h4 mt-4 mb-2">Orders</h2>
      <p className="text-secondary small mb-3">Fetches from your order history; apply filters locally.</p>
      {ordersError ? <p className="text-danger small mb-3">{ordersError}</p> : null}

      <Card className="border-secondary mb-3">
        <Card.Body className="p-2">
          <Row className="g-2">
            <Col xs={12} md={4}>
              <Form.Label className="small text-secondary text-uppercase mb-1">Search</Form.Label>
              <Form.Control
                size="sm"
                value={orderSearch}
                onChange={(e) => setOrderSearch(e.target.value)}
                placeholder="External ID / status / bot id"
              />
            </Col>
            <Col xs={6} md={2}>
              <Form.Label className="small text-secondary text-uppercase mb-1">Status</Form.Label>
              <Form.Select size="sm" value={orderStatus} onChange={(e) => setOrderStatus(e.target.value)}>
                <option value="all">All</option>
                {orderStatusOptions.map((s) => (
                  <option key={`order-status-${s}`} value={s}>
                    {s}
                  </option>
                ))}
              </Form.Select>
            </Col>
            <Col xs={6} md={2}>
              <Form.Label className="small text-secondary text-uppercase mb-1">Bot</Form.Label>
              <Form.Select size="sm" value={orderBotId} onChange={(e) => setOrderBotId(e.target.value)}>
                <option value="all">All</option>
                {orderBotOptions.map((b) => (
                  <option key={`order-bot-${b}`} value={b}>
                    {b.slice(0, 8)}...
                  </option>
                ))}
              </Form.Select>
            </Col>
            <Col xs={6} md={2}>
              <Form.Label className="small text-secondary text-uppercase mb-1">From</Form.Label>
              <Form.Control size="sm" type="datetime-local" value={orderFrom} onChange={(e) => setOrderFrom(e.target.value)} />
            </Col>
            <Col xs={6} md={2}>
              <Form.Label className="small text-secondary text-uppercase mb-1">To</Form.Label>
              <Form.Control size="sm" type="datetime-local" value={orderTo} onChange={(e) => setOrderTo(e.target.value)} />
            </Col>
          </Row>
        </Card.Body>
      </Card>

      <div className="rounded border border-secondary overflow-x-auto">
        <Table responsive hover className="mb-0 small">
          <thead className="table-dark">
            <tr>
              <th>Created</th>
              <th>External ID</th>
              <th>Status</th>
              <th>Bot ID</th>
            </tr>
          </thead>
          <tbody>
            {filteredOrders.map((o) => (
              <tr key={o.id}>
                <td className="text-secondary text-nowrap">{formatLocalDateTime(o.createdAt)}</td>
                <td className="font-monospace">{o.externalId}</td>
                <td>{o.status}</td>
                <td className="font-monospace">{o.botId}</td>
              </tr>
            ))}
            {filteredOrders.length === 0 ? (
              <tr>
                <td colSpan={4} className="text-muted">
                  No orders match current filters.
                </td>
              </tr>
            ) : null}
          </tbody>
        </Table>
      </div>
    </Layout>
  )
}
