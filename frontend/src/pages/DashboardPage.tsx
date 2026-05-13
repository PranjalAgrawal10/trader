import { useEffect, useMemo, useState } from 'react'
import { Card, Col, Row } from 'react-bootstrap'
import { api } from '../api/client'
import { ChartWithRightGutter } from '../components/ChartWithRightGutter'
import { LwTimeLine } from '../components/LwMiscCharts'
import { Layout } from '../components/Layout'

interface BotRow {
  id: string
  strategyId: string | null
  status: string | number
}

interface StrategyRow {
  id: string
  name: string
}

interface TradeRow {
  id: string
  executedAt: string
  price: number
  symbol?: string
  realizedPnl?: number | null
}

export function DashboardPage() {
  const [bots, setBots] = useState<BotRow[]>([])
  const [strategies, setStrategies] = useState<StrategyRow[]>([])
  const [trades, setTrades] = useState<TradeRow[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const [b, s, t] = await Promise.all([
          api.get<BotRow[]>('/bots'),
          api.get<StrategyRow[]>('/strategies'),
          api.get<TradeRow[]>('/trades'),
        ])
        if (!cancelled) {
          setBots(b.data)
          setStrategies(s.data)
          setTrades(t.data.slice(0, 20))
        }
      } catch {
        if (!cancelled) setError('Could not load dashboard data.')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  const hasRealizedPnl = trades.some(
    (t) => t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)),
  )

  const lwCumulativePoints = useMemo(() => {
    const out: { timeMs: number; value: number }[] = []
    let lastTs = 0
    let carry = 0
    const sorted = [...trades].sort(
      (a, b) => new Date(a.executedAt).getTime() - new Date(b.executedAt).getTime(),
    )
    for (const t of sorted) {
      const ts = Math.max(lastTs + 1, new Date(t.executedAt).getTime())
      lastTs = ts
      const leg =
        t.realizedPnl != null && Number.isFinite(Number(t.realizedPnl)) ? Number(t.realizedPnl) : null
      if (leg != null) carry += leg
      out.push({ timeMs: ts, value: carry })
    }
    return out
  }, [trades])

  const lwPricePoints = useMemo(() => {
    const sorted = [...trades].sort(
      (a, b) => new Date(a.executedAt).getTime() - new Date(b.executedAt).getTime(),
    )
    let lastTs = 0
    return sorted.map((t) => {
      const raw = new Date(t.executedAt).getTime()
      const ts = raw <= lastTs ? lastTs + 1 : raw
      lastTs = ts
      return { timeMs: ts, value: Number(t.price) }
    })
  }, [trades])

  const isRunning = (s: BotRow['status']) =>
    s === 2 || s === 'Running' || s === 'running'

  const running = bots.filter((b) => isRunning(b.status)).length

  return (
    <Layout>
      <h1 className="h3 mb-1">Dashboard</h1>
      <p className="text-secondary small mb-4">
        Overview of bots, strategies, and recent fills (paper or live).
      </p>

      {error ? <p className="text-danger small mb-4">{error}</p> : null}

      <Row className="g-3 mb-4">
        <Col sm={6} lg={4}>
          <Card className="border-secondary h-100">
            <Card.Body>
              <Card.Subtitle className="text-secondary text-uppercase small">Running bots</Card.Subtitle>
              <Card.Title className="display-6 text-success mt-2 mb-0">{running}</Card.Title>
              <Card.Text className="small text-secondary mb-0">of {bots.length} total</Card.Text>
            </Card.Body>
          </Card>
        </Col>
        <Col sm={6} lg={4}>
          <Card className="border-secondary h-100">
            <Card.Body>
              <Card.Subtitle className="text-secondary text-uppercase small">Strategies</Card.Subtitle>
              <Card.Title className="display-6 text-info mt-2 mb-0">{strategies.length}</Card.Title>
              <Card.Text className="small text-secondary mb-0">configured profiles</Card.Text>
            </Card.Body>
          </Card>
        </Col>
        <Col sm={6} lg={4}>
          <Card className="border-secondary h-100">
            <Card.Body>
              <Card.Subtitle className="text-secondary text-uppercase small">Recent trades</Card.Subtitle>
              <Card.Title className="display-6 text-warning mt-2 mb-0">{trades.length}</Card.Title>
              <Card.Text className="small text-secondary mb-0">latest window</Card.Text>
            </Card.Body>
          </Card>
        </Col>
      </Row>

      <Card className="border-secondary">
        <Card.Body>
          <Card.Subtitle className="text-body-secondary mb-3">
            {hasRealizedPnl ? 'Cumulative realized P&L (trade window)' : 'Recent trade prices'}
          </Card.Subtitle>
          <div style={{ height: '16rem' }}>
            {trades.length === 0 ? (
              <p className="text-secondary small mb-0">No trades yet. Start a bot or ingest logs.</p>
            ) : hasRealizedPnl ? (
              <ChartWithRightGutter>
                <LwTimeLine points={lwCumulativePoints} heightPx={256} stroke="#fd7e14" zeroBaseline />
              </ChartWithRightGutter>
            ) : (
              <ChartWithRightGutter>
                <LwTimeLine points={lwPricePoints} heightPx={256} stroke="#198754" />
              </ChartWithRightGutter>
            )}
          </div>
          {!hasRealizedPnl && trades.length > 0 ? (
            <p className="text-muted small mb-0 mt-2">
              Realized P&amp;L will replace this chart when fills include non-empty P&amp;L values.
            </p>
          ) : null}
        </Card.Body>
      </Card>
    </Layout>
  )
}
