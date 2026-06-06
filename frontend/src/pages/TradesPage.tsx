import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, Card, Col, Dropdown, Form, Row, Table } from 'react-bootstrap'
import { useNavigate } from 'react-router-dom'
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
  orderId: string
  exchangeOrderId: string | null
  parentOrderId: string | null
  status: string
  statusMessage: string | null
  statusMessageRaw: string | null
  tradingsymbol: string
  exchange: string
  transactionType: string
  variety: string
  orderType: string
  product: string
  validity: string
  quantity: number
  filledQuantity: number
  pendingQuantity: number
  cancelledQuantity: number | null
  price: number | null
  triggerPrice: number | null
  averagePrice: number | null
  tag: string | null
  orderTimestamp: string | null
  exchangeUpdateTimestamp: string | null
}

interface KiteOrderBookResponse {
  items: OrderRow[]
}

interface KiteOrderActionResult {
  orderId: string
  action: string
  message: string
}

const KITE_ORDER_STATUSES: readonly string[] = [
  'OPEN',
  'COMPLETE',
  'CANCELLED',
  'REJECTED',
  'PUT ORDER REQ RECEIVED',
  'VALIDATION PENDING',
  'OPEN PENDING',
  'MODIFY VALIDATION PENDING',
  'MODIFY PENDING',
  'TRIGGER PENDING',
  'CANCEL PENDING',
  'AMO REQ RECEIVED',
]

function normalizeKiteTimestamp(value: string | null | undefined): string | null {
  if (!value) return null
  const t = value.trim()
  if (!t) return null
  if (t.includes('T')) return t
  // Kite orders API often returns "yyyy-MM-dd HH:mm:ss" without timezone; treat as IST.
  return t.replace(' ', 'T') + '+05:30'
}

export function TradesPage() {
  const navigate = useNavigate()
  const [rows, setRows] = useState<TradeRow[]>([])
  const [orders, setOrders] = useState<OrderRow[]>([])
  const [error, setError] = useState<string | null>(null)
  const [ordersError, setOrdersError] = useState<string | null>(null)
  const [ordersActionInfo, setOrdersActionInfo] = useState<string | null>(null)
  const [orderSearch, setOrderSearch] = useState('')
  const [orderStatus, setOrderStatus] = useState('all')
  const [orderBotId, setOrderBotId] = useState('all')
  const [orderType, setOrderType] = useState('all')
  const [orderFrom, setOrderFrom] = useState('')
  const [orderTo, setOrderTo] = useState('')

  const loadOrders = useCallback(async () => {
    try {
      const r = await api.get<KiteOrderBookResponse>('/broker/kite/orders')
      setOrders(r.data.items ?? [])
      setOrdersError(null)
    } catch {
      setOrdersError('Failed to load orders.')
    }
  }, [])

  useEffect(() => {
    api
      .get<TradeRow[]>('/trades')
      .then((r) => setRows(r.data))
      .catch(() => setError('Failed to load trades.'))
    void loadOrders()
  }, [loadOrders])

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

  const orderStatusOptions = useMemo(() => {
    const seen = new Set(KITE_ORDER_STATUSES)
    for (const o of orders) {
      const s = o.status.trim()
      if (s.length > 0) seen.add(s)
    }
    return [...seen]
  }, [orders])

  const orderExchangeOptions = useMemo(
    () => [...new Set(orders.map((o) => o.exchange.trim()).filter((s) => s.length > 0))].sort((a, b) => a.localeCompare(b)),
    [orders],
  )

  const orderTypeOptions = useMemo(
    () =>
      [...new Set(orders.map((o) => `${o.transactionType} ${o.orderType}`.trim()).filter((s) => s.length > 0))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [orders],
  )

  const filteredOrders = useMemo(() => {
    const q = orderSearch.trim().toLowerCase()
    const fromMs = orderFrom ? new Date(orderFrom).getTime() : null
    const toMs = orderTo ? new Date(orderTo).getTime() : null
    return orders.filter((o) => {
      if (orderStatus !== 'all' && o.status !== orderStatus) return false
      if (orderBotId !== 'all' && o.exchange !== orderBotId) return false
      if (orderType !== 'all' && `${o.transactionType} ${o.orderType}`.trim() !== orderType) return false
      if (q.length > 0) {
        const hay =
          `${o.orderId} ${o.exchangeOrderId ?? ''} ${o.tradingsymbol} ${o.exchange} ${o.status} ${o.transactionType} ${o.orderType} ${o.product} ${o.statusMessage ?? ''}`.toLowerCase()
        if (!hay.includes(q)) return false
      }
      const createdMs = new Date(normalizeKiteTimestamp(o.exchangeUpdateTimestamp ?? o.orderTimestamp) ?? '').getTime()
      if (fromMs != null && Number.isFinite(fromMs) && createdMs < fromMs) return false
      if (toMs != null && Number.isFinite(toMs) && createdMs > toMs) return false
      return true
    })
  }, [orderBotId, orderFrom, orderSearch, orderStatus, orderTo, orderType, orders])

  const orderActions = [
    'Modify',
    'Cancel',
    'Repeat',
    'Info',
    'Chart',
    'Option chain',
    'Create GTT / GTC',
    'Create alert / ATO',
    'Market depth',
    'Add to marketwatch',
    'Add to basket',
    'Technicals',
  ] as const

  const onOrderAction = async (action: (typeof orderActions)[number], order: OrderRow) => {
    if (action === 'Cancel') {
      try {
        const res = await api.post<KiteOrderActionResult>(`/broker/kite/orders/${encodeURIComponent(order.orderId)}/cancel`, {
          variety: order.variety,
          parentOrderId: order.parentOrderId,
        })
        setOrdersActionInfo(res.data.message)
        await loadOrders()
      } catch {
        setOrdersActionInfo(`Cancel failed for ${order.orderId}.`)
      }
      return
    }
    if (action === 'Modify') {
      try {
        const res = await api.post<KiteOrderActionResult>(`/broker/kite/orders/${encodeURIComponent(order.orderId)}/modify`, {
          variety: order.variety,
          exchange: order.exchange,
          tradingsymbol: order.tradingsymbol,
          transactionType: order.transactionType,
          quantity: order.quantity,
          product: order.product,
          orderType: order.orderType,
          validity: order.validity,
          price: order.price,
          triggerPrice: order.triggerPrice,
          tag: order.tag,
        })
        setOrdersActionInfo(res.data.message)
        await loadOrders()
      } catch {
        setOrdersActionInfo(`Modify failed for ${order.orderId}.`)
      }
      return
    }
    if (action === 'Repeat') {
      try {
        const res = await api.post<KiteOrderActionResult>(`/broker/kite/orders/${encodeURIComponent(order.orderId)}/repeat`, {
          variety: order.variety,
        })
        setOrdersActionInfo(`${res.data.message} New order: ${res.data.orderId}`)
        await loadOrders()
      } catch {
        setOrdersActionInfo(`Repeat failed for ${order.orderId}.`)
      }
      return
    }
    if (action === 'Chart') {
      navigate('/instruments')
      setOrdersActionInfo(`Opened Instruments for ${order.tradingsymbol}.`)
      return
    }
    if (action === 'Option chain' || action === 'Technicals') {
      navigate('/scalper')
      setOrdersActionInfo(`Opened Scalper for ${order.tradingsymbol}.`)
      return
    }
    if (action === 'Info') {
      setOrderSearch(order.orderId)
      setOrdersActionInfo(`Loaded order ${order.orderId} details in filters/table.`)
      return
    }
    if (action === 'Add to marketwatch') {
      try {
        await navigator.clipboard.writeText(`${order.exchange}:${order.tradingsymbol}`)
        setOrdersActionInfo(`Copied ${order.exchange}:${order.tradingsymbol} to clipboard (use in watch/search).`)
      } catch {
        setOrdersActionInfo(`Use ${order.exchange}:${order.tradingsymbol} in watch/search.`)
      }
      return
    }
    if (action === 'Add to basket') {
      try {
        await navigator.clipboard.writeText(
          JSON.stringify(
            {
              tradingsymbol: order.tradingsymbol,
              exchange: order.exchange,
              transactionType: order.transactionType,
              orderType: order.orderType,
              product: order.product,
              quantity: order.quantity,
              price: order.price,
              triggerPrice: order.triggerPrice,
            },
            null,
            2,
          ),
        )
        setOrdersActionInfo(`Copied order payload for ${order.tradingsymbol} to clipboard.`)
      } catch {
        setOrdersActionInfo(`Basket payload ready for ${order.tradingsymbol}.`)
      }
      return
    }
    if (action === 'Market depth') {
      setOrdersActionInfo(
        `${order.tradingsymbol}: qty ${order.quantity}, filled ${order.filledQuantity}, pending ${order.pendingQuantity}, status ${order.status}.`,
      )
      return
    }
    setOrdersActionInfo(`${action} is available in the action menu.`)
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

      <h2 className="h4 mt-4 mb-2">Orders</h2>
      <p className="text-secondary small mb-3">Fetches from your order history; apply filters locally.</p>
      {ordersError ? <p className="text-danger small mb-3">{ordersError}</p> : null}
      {ordersActionInfo ? (
        <Alert variant="secondary" className="py-2 small mb-3">
          {ordersActionInfo}
        </Alert>
      ) : null}

      <Card className="border-secondary mb-3">
        <Card.Body className="p-2">
          <Row className="g-2">
            <Col xs={12} md={4}>
              <Form.Label className="small text-secondary text-uppercase mb-1">Search</Form.Label>
              <Form.Control
                size="sm"
                value={orderSearch}
                onChange={(e) => setOrderSearch(e.target.value)}
                placeholder="Order ID / symbol / status / type"
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
              <Form.Label className="small text-secondary text-uppercase mb-1">Exchange</Form.Label>
              <Form.Select size="sm" value={orderBotId} onChange={(e) => setOrderBotId(e.target.value)}>
                <option value="all">All</option>
                {orderExchangeOptions.map((b) => (
                  <option key={`order-ex-${b}`} value={b}>
                    {b}
                  </option>
                ))}
              </Form.Select>
            </Col>
            <Col xs={6} md={2}>
              <Form.Label className="small text-secondary text-uppercase mb-1">Type</Form.Label>
              <Form.Select size="sm" value={orderType} onChange={(e) => setOrderType(e.target.value)}>
                <option value="all">All</option>
                {orderTypeOptions.map((t) => (
                  <option key={`order-type-${t}`} value={t}>
                    {t}
                  </option>
                ))}
              </Form.Select>
            </Col>
            <Col xs={6} md={1}>
              <Form.Label className="small text-secondary text-uppercase mb-1">From</Form.Label>
              <Form.Control size="sm" type="datetime-local" value={orderFrom} onChange={(e) => setOrderFrom(e.target.value)} />
            </Col>
            <Col xs={6} md={1}>
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
              <th>Updated</th>
              <th>Order ID</th>
              <th>Symbol</th>
              <th>Type</th>
              <th>Status</th>
              <th>Qty (F/P/C)</th>
              <th>Price / Avg / Trigger</th>
              <th>Message</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filteredOrders.map((o) => (
              <tr key={o.orderId}>
                <td className="text-secondary text-nowrap">
                  {formatLocalDateTime(normalizeKiteTimestamp(o.exchangeUpdateTimestamp ?? o.orderTimestamp))}
                </td>
                <td className="font-monospace">
                  {o.orderId}
                  {o.exchangeOrderId ? <div className="text-muted">{o.exchangeOrderId}</div> : null}
                </td>
                <td>
                  <div className="fw-medium">{o.tradingsymbol}</div>
                  <div className="text-muted small">{o.exchange}</div>
                </td>
                <td className="text-nowrap">
                  {o.transactionType} {o.orderType}
                  <div className="text-muted small">{o.product}</div>
                </td>
                <td className="text-nowrap">{o.status}</td>
                <td className="text-nowrap">{o.quantity} ({o.filledQuantity}/{o.pendingQuantity}/{o.cancelledQuantity ?? 0})</td>
                <td className="text-nowrap">
                  {o.price ?? '—'} / {o.averagePrice ?? '—'} / {o.triggerPrice ?? '—'}
                </td>
                <td className="small">{o.statusMessage ?? o.statusMessageRaw ?? '—'}</td>
                <td className="text-nowrap">
                  <Dropdown align="end">
                    <Dropdown.Toggle size="sm" variant="outline-secondary" id={`order-actions-${o.orderId}`}>
                      Actions
                    </Dropdown.Toggle>
                    <Dropdown.Menu>
                      {orderActions.map((action) => (
                        <Dropdown.Item key={`${o.orderId}-${action}`} onClick={() => void onOrderAction(action, o)}>
                          {action}
                        </Dropdown.Item>
                      ))}
                    </Dropdown.Menu>
                  </Dropdown>
                </td>
              </tr>
            ))}
            {filteredOrders.length === 0 ? (
              <tr>
                <td colSpan={9} className="text-muted">
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
