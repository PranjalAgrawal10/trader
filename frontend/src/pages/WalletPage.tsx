import axios from 'axios'
import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Form, Row, Spinner } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { Layout } from '../components/Layout'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'

const inrFmt = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

interface BrokerStatusResponse {
  connected: boolean
  provider: string | null
}

interface KiteMarginSegment {
  enabled: boolean
  net: number
  availableCash: number
  liveBalance: number
  openingBalance: number
  intradayPayin: number
  utilisedDebits: number
}

interface KiteUserMarginsResponse {
  equity: KiteMarginSegment | null
  commodity: KiteMarginSegment | null
}

function problemDetail(err: unknown): string | null {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    const s = body?.detail ?? body?.title ?? (err.response?.status === 401 ? err.message : null)
    return s && s.length > 0 ? s : null
  }
  return null
}

function MarginSegmentTable({ label, segment }: { label: string; segment: KiteMarginSegment }) {
  if (!segment.enabled) {
    return (
      <p className="small text-secondary mb-0">
        {label}: segment not enabled on your Kite account.
      </p>
    )
  }

  return (
    <div className="mb-3">
      <div className="small fw-semibold mb-1">{label}</div>
      <div className="small font-monospace d-grid gap-1" style={{ gridTemplateColumns: '1fr auto' }}>
        <span className="text-secondary">Net (available to trade)</span>
        <span>{inrFmt.format(segment.net)}</span>
        <span className="text-secondary">Live balance</span>
        <span>{inrFmt.format(segment.liveBalance)}</span>
        <span className="text-secondary">Available cash</span>
        <span>{inrFmt.format(segment.availableCash)}</span>
        <span className="text-secondary">Opening balance</span>
        <span>{inrFmt.format(segment.openingBalance)}</span>
        {segment.intradayPayin > 0 ? (
          <>
            <span className="text-secondary">Intraday pay-in</span>
            <span>{inrFmt.format(segment.intradayPayin)}</span>
          </>
        ) : null}
        {segment.utilisedDebits > 0 ? (
          <>
            <span className="text-secondary">Utilised margin</span>
            <span>{inrFmt.format(segment.utilisedDebits)}</span>
          </>
        ) : null}
      </div>
    </div>
  )
}

export function WalletPage() {
  const [balance, setBalance] = useState<number | null>(null)
  const [kiteMargins, setKiteMargins] = useState<KiteUserMarginsResponse | null>(null)
  const [kiteConnected, setKiteConnected] = useState(false)
  const [loading, setLoading] = useState(true)
  const [kiteLoading, setKiteLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [amountText, setAmountText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [kiteError, setKiteError] = useState<string | null>(null)

  const loadKiteMargins = useCallback(async (connected: boolean, provider: string | null) => {
    const isZerodha = connected && (provider ?? '').toLowerCase() === 'zerodha'
    setKiteConnected(isZerodha)
    if (!isZerodha) {
      setKiteMargins(null)
      setKiteError(null)
      return
    }

    setKiteLoading(true)
    setKiteError(null)
    try {
      const { data } = await api.get<KiteUserMarginsResponse>('/broker/kite/margins')
      setKiteMargins(data)
    } catch (e) {
      setKiteMargins(null)
      setKiteError(problemDetail(e) ?? 'Could not load Kite balance.')
    } finally {
      setKiteLoading(false)
    }
  }, [])

  const loadWallet = useCallback(async () => {
    setError(null)
    setLoading(true)
    try {
      const [walletRes, brokerRes] = await Promise.all([
        api.get<{ balance: number }>('/wallet'),
        api.get<BrokerStatusResponse>('/broker/status'),
      ])
      setBalance(walletRes.data.balance)
      await loadKiteMargins(brokerRes.data.connected, brokerRes.data.provider)
    } catch (e) {
      setBalance(null)
      setError(problemDetail(e) ?? 'Could not load wallet balance.')
    } finally {
      setLoading(false)
    }
  }, [loadKiteMargins])

  useEffect(() => {
    void loadWallet()
  }, [loadWallet])

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

  const refreshKite = () => {
    void loadKiteMargins(kiteConnected, kiteConnected ? 'zerodha' : null)
  }

  return (
    <Layout>
      <Row className="justify-content-center g-3">
        <Col xs={12} md={8} lg={6}>
          <h1 className="h4 mb-3">Wallet</h1>

          <Card className="border-secondary shadow-sm mb-3">
            <Card.Body>
              <Card.Title className="h6 mb-3">Zerodha Kite balance</Card.Title>
              <p className="text-secondary small mb-3">
                Live funds and margin from your linked Zerodha Kite account.
              </p>
              {kiteError ? (
                <Alert variant="warning" className="py-2 small">
                  {kiteError}
                </Alert>
              ) : null}
              {!kiteConnected && !kiteLoading && !loading ? (
                <p className="small text-secondary mb-0">
                  Connect Zerodha under{' '}
                  <Link to={`/profile#${BROKER_PROFILE_SECTION_ID}`}>Profile → Brokers</Link> to see your Kite balance here.
                </p>
              ) : null}
              {kiteLoading || (loading && kiteConnected) ? (
                <div className="d-flex align-items-center gap-2 text-secondary">
                  <Spinner animation="border" size="sm" />
                  Loading Kite balance…
                </div>
              ) : kiteConnected && kiteMargins ? (
                <>
                  {kiteMargins.equity ? <MarginSegmentTable label="Equity (F&O / cash)" segment={kiteMargins.equity} /> : null}
                  {kiteMargins.commodity ? <MarginSegmentTable label="Commodity (MCX)" segment={kiteMargins.commodity} /> : null}
                  {!kiteMargins.equity && !kiteMargins.commodity ? (
                    <p className="small text-secondary mb-0">No margin segments returned from Kite.</p>
                  ) : null}
                  <Button type="button" variant="outline-secondary" size="sm" onClick={refreshKite} disabled={kiteLoading}>
                    Refresh Kite balance
                  </Button>
                </>
              ) : null}
            </Card.Body>
          </Card>

          <Card className="border-secondary shadow-sm">
            <Card.Body>
              <Card.Title className="h6 mb-3">Paper balance</Card.Title>
              <p className="text-secondary small mb-3">
                Simulated INR for demo auto-trade and manual paper trades — not your broker account. Add funds manually; there is no payment gateway.
              </p>
              {error ? (
                <Alert variant="danger" className="py-2 small">
                  {error}
                </Alert>
              ) : null}
              {loading ? (
                <div className="d-flex align-items-center gap-2 text-secondary">
                  <Spinner animation="border" size="sm" />
                  Loading paper balance…
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
                      <Button
                        type="button"
                        variant="outline-secondary"
                        size="sm"
                        onClick={() => void loadWallet()}
                        disabled={submitting || loading}
                      >
                        Refresh all
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
