import { type FormEvent, useEffect, useState } from 'react'
import { Button, Card, Col, Form, Row, Stack } from 'react-bootstrap'
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

export function BotsPage() {
  const [bots, setBots] = useState<BotRow[]>([])
  const [strategies, setStrategies] = useState<StrategyRow[]>([])
  const [strategyId, setStrategyId] = useState('')
  const [message, setMessage] = useState<string | null>(null)

  const load = async () => {
    const [b, s] = await Promise.all([
      api.get<BotRow[]>('/bots'),
      api.get<StrategyRow[]>('/strategies'),
    ])
    setBots(b.data)
    setStrategies(s.data)
    if (!strategyId && s.data.length > 0) setStrategyId(s.data[0].id)
  }

  useEffect(() => {
    load().catch(() => setMessage('Failed to load bots.'))
  }, [])

  const createBot = async (e: FormEvent) => {
    e.preventDefault()
    setMessage(null)
    try {
      await api.post('/bots', {
        strategyId: strategyId || null,
      })
      await load()
    } catch {
      setMessage('Could not create bot (check strategy).')
    }
  }

  const assign = async (botId: string) => {
    if (!strategyId) return
    setMessage(null)
    try {
      await api.post(`/bots/${botId}/assign-strategy`, { strategyId })
      await load()
    } catch {
      setMessage('Assign strategy failed.')
    }
  }

  const start = async (botId: string) => {
    setMessage(null)
    try {
      await api.post(`/bots/${botId}/start`)
      await load()
    } catch {
      setMessage('Start failed (strategy required).')
    }
  }

  const stop = async (botId: string) => {
    setMessage(null)
    try {
      await api.post(`/bots/${botId}/stop`)
      await load()
    } catch {
      setMessage('Stop failed.')
    }
  }

  const statusLabel = (s: string | number) => {
    const v = typeof s === 'string' ? s.toLowerCase() : s
    if (v === 'running' || v === 2) return 'Running'
    if (v === 'stopped' || v === 0) return 'Stopped'
    if (v === 'starting' || v === 1) return 'Starting'
    if (v === 'error' || v === 3) return 'Error'
    return String(s)
  }

  return (
    <Layout>
      <h1 className="h3 mb-1">Bots</h1>
      <p className="text-secondary small mb-4">Create runners, bind strategies, and toggle lifecycle.</p>

      <Card className="border-secondary mb-4">
        <Card.Body>
          <Form onSubmit={createBot}>
            <Row className="align-items-end g-3">
              <Col xs={12} md="auto">
                <Form.Group controlId="bot-strategy">
                  <Form.Label className="small text-secondary text-uppercase">Strategy</Form.Label>
                  <Form.Select value={strategyId} onChange={(e) => setStrategyId(e.target.value)}>
                    <option value="">None</option>
                    {strategies.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </Form.Select>
                </Form.Group>
              </Col>
              <Col xs={12} md="auto">
                <Button variant="success" type="submit">
                  Add bot
                </Button>
              </Col>
              {message ? (
                <Col xs={12}>
                  <p className="text-danger small mb-0">{message}</p>
                </Col>
              ) : null}
            </Row>
          </Form>
        </Card.Body>
      </Card>

      <Stack gap={3}>
        {bots.map((b) => (
          <Card key={b.id} className="border-secondary">
            <Card.Body className="d-flex flex-column flex-md-row justify-content-between gap-3">
              <div>
                <p className="font-monospace small text-secondary mb-1">{b.id}</p>
                <p className="small mb-1">
                  Strategy: <span className="font-monospace text-success">{b.strategyId ?? '—'}</span>
                </p>
                <p className="small mb-0">
                  <span className="text-secondary text-uppercase">Status:</span>{' '}
                  <span className="text-body">{statusLabel(b.status)}</span>
                </p>
              </div>
              <Stack direction="horizontal" gap={2} className="flex-wrap">
                <Button variant="secondary" size="sm" onClick={() => assign(b.id)}>
                  Assign selected
                </Button>
                <Button variant="info" size="sm" onClick={() => start(b.id)}>
                  Start
                </Button>
                <Button variant="danger" size="sm" onClick={() => stop(b.id)}>
                  Stop
                </Button>
              </Stack>
            </Card.Body>
          </Card>
        ))}
      </Stack>

      {bots.length === 0 ? <p className="text-secondary small mt-3 mb-0">No bots yet. Create one above.</p> : null}
    </Layout>
  )
}
