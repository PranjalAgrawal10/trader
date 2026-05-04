import { useEffect, useState } from 'react'
import {
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Card, Col, Row } from 'react-bootstrap'
import { api } from '../api/client'
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

  const chartData = [...trades]
    .reverse()
    .map((x, i) => ({ i: i + 1, price: Number(x.price) }))

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
          <Card.Subtitle className="text-body-secondary mb-3">Recent trade prices</Card.Subtitle>
          <div style={{ height: '16rem' }}>
            {chartData.length === 0 ? (
              <p className="text-secondary small mb-0">No trades yet. Start a bot or ingest logs.</p>
            ) : (
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={chartData}>
                  <XAxis dataKey="i" stroke="#adb5bd" tick={{ fontSize: 11 }} />
                  <YAxis stroke="#adb5bd" tick={{ fontSize: 11 }} domain={['auto', 'auto']} />
                  <Tooltip
                    contentStyle={{
                      background: '#212529',
                      border: '1px solid #495057',
                      borderRadius: 8,
                    }}
                  />
                  <Line type="monotone" dataKey="price" stroke="#198754" dot={false} strokeWidth={2} />
                </LineChart>
              </ResponsiveContainer>
            )}
          </div>
        </Card.Body>
      </Card>
    </Layout>
  )
}
