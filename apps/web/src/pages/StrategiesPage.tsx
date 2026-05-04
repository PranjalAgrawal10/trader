import { type FormEvent, useEffect, useState } from 'react'
import { Button, Card, Col, Form, Row, Table } from 'react-bootstrap'
import { api } from '../api/client'
import { Layout } from '../components/Layout'

interface StrategyRow {
  id: string
  name: string
  parametersJson: string
  createdAt: string
}

export function StrategiesPage() {
  const [items, setItems] = useState<StrategyRow[]>([])
  const [name, setName] = useState('RSI breakout')
  const [params, setParams] = useState('{"rsiPeriod":14,"oversold":30}')
  const [message, setMessage] = useState<string | null>(null)

  const load = async () => {
    const { data } = await api.get<StrategyRow[]>('/strategies')
    setItems(data)
  }

  useEffect(() => {
    load().catch(() => setMessage('Failed to load strategies.'))
  }, [])

  const create = async (e: FormEvent) => {
    e.preventDefault()
    setMessage(null)
    try {
      await api.post('/strategies', { name, parametersJson: params })
      setName('New strategy')
      await load()
    } catch {
      setMessage('Could not create strategy.')
    }
  }

  const remove = async (id: string) => {
    setMessage(null)
    try {
      await api.delete(`/strategies/${id}`)
      await load()
    } catch {
      setMessage('Could not delete strategy.')
    }
  }

  return (
    <Layout>
      <h1 className="h3 mb-1">Strategies</h1>
      <p className="text-secondary small mb-4">Store indicator parameters as JSON for the bot service.</p>

      <Card className="border-secondary mb-4">
        <Card.Body>
          <Form onSubmit={create}>
            <Row className="g-3">
              <Col md={6}>
                <Form.Group controlId="strategy-name">
                  <Form.Label className="small text-secondary text-uppercase">Name</Form.Label>
                  <Form.Control value={name} onChange={(e) => setName(e.target.value)} required />
                </Form.Group>
              </Col>
            </Row>
            <Form.Group className="mt-3 mb-3" controlId="strategy-params">
              <Form.Label className="small text-secondary text-uppercase">Parameters (JSON)</Form.Label>
              <Form.Control
                as="textarea"
                rows={6}
                className="font-monospace small"
                value={params}
                onChange={(e) => setParams(e.target.value)}
              />
            </Form.Group>
            {message ? <p className="text-danger small">{message}</p> : null}
            <Button variant="success" type="submit">
              Save strategy
            </Button>
          </Form>
        </Card.Body>
      </Card>

      <div className="rounded border border-secondary overflow-hidden">
        <Table responsive hover className="mb-0 align-middle small">
          <thead className="table-dark">
            <tr>
              <th>Name</th>
              <th>Parameters</th>
              <th className="text-end">Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.map((s) => (
              <tr key={s.id}>
                <td className="fw-medium">{s.name}</td>
                <td className="font-monospace text-secondary text-truncate" style={{ maxWidth: '20rem' }}>
                  {s.parametersJson}
                </td>
                <td className="text-end">
                  <Button variant="link" size="sm" className="text-danger p-0" onClick={() => remove(s.id)}>
                    Delete
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </Table>
      </div>
    </Layout>
  )
}
