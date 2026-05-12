import axios from 'axios'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Form, Row, Spinner } from 'react-bootstrap'
import { api } from '../api/client'
import { Layout } from '../components/Layout'

const inrFmt = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

function problemDetail(err: unknown): string | null {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    const s = body?.detail ?? body?.title ?? (err.response?.status === 401 ? err.message : null)
    return s && s.length > 0 ? s : null
  }
  return null
}

export function WalletPage() {
  const [balance, setBalance] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [amountText, setAmountText] = useState('')
  const [error, setError] = useState<string | null>(null)

  const loadBalance = useCallback(async () => {
    setError(null)
    setLoading(true)
    try {
      const { data } = await api.get<{ balance: number }>('/wallet')
      setBalance(data.balance)
    } catch (e) {
      setBalance(null)
      setError(problemDetail(e) ?? 'Could not load wallet balance.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadBalance()
  }, [loadBalance])

  const onSubmit = async (ev: FormEvent) => {
    ev.preventDefault()
    setError(null)
    const n = Number(amountText.trim().replace(',', ''))
    if (!Number.isFinite(n) || n <= 0) {
      setError('Enter a positive amount.')
      return
    }

    setSubmitting(true)
    try {
      const { data } = await api.post<{ balance: number }>('/wallet/load', { amount: n })
      setBalance(data.balance)
      setAmountText('')
    } catch (e) {
      setError(problemDetail(e) ?? 'Could not add funds.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Layout>
      <Row className="justify-content-center">
        <Col xs={12} md={8} lg={6}>
          <h1 className="h4 mb-3">Wallet</h1>
          <Card className="border-secondary shadow-sm">
            <Card.Body>
              <Card.Title className="h6 mb-3">Paper balance</Card.Title>
              <p className="text-secondary small mb-3">
                Add funds manually for now—there is no payment gateway. Amounts are stored in INR (simulated).
              </p>
              {error ? (
                <Alert variant="danger" className="py-2 small">
                  {error}
                </Alert>
              ) : null}
              {loading ? (
                <div className="d-flex align-items-center gap-2 text-secondary">
                  <Spinner animation="border" size="sm" />
                  Loading balance…
                </div>
              ) : (
                <>
                  <p className="fs-5 mb-4">{inrFmt.format(balance ?? 0)}</p>
                  <Form onSubmit={(e) => void onSubmit(e)}>
                    <Form.Group className="mb-3" controlId="wallet-amount">
                      <Form.Label>Amount to add</Form.Label>
                      <Form.Control
                        type="text"
                        inputMode="decimal"
                        autoComplete="off"
                        placeholder="e.g. 10000"
                        value={amountText}
                        onChange={(e) => setAmountText(e.target.value)}
                        disabled={submitting}
                      />
                    </Form.Group>
                    <div className="d-flex gap-2 flex-wrap">
                      <Button type="submit" variant="success" disabled={submitting}>
                        {submitting ? (
                          <>
                            <Spinner animation="border" size="sm" className="me-2 align-middle" />
                            Adding…
                          </>
                        ) : (
                          'Add funds'
                        )}
                      </Button>
                      <Button type="button" variant="outline-secondary" size="sm" onClick={() => void loadBalance()} disabled={submitting}>
                        Refresh
                      </Button>
                    </div>
                  </Form>
                </>
              )}
            </Card.Body>
          </Card>
        </Col>
      </Row>
    </Layout>
  )
}
