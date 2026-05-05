import axios from 'axios'
import { useCallback, useEffect, useState } from 'react'
import { Alert, Button, Card, Stack } from 'react-bootstrap'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { Layout } from '../components/Layout'

interface BrokerStatusResponse {
  connected: boolean
  connectedAt: string | null
  provider: string | null
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

export function BrokersPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const setup = searchParams.get('setup') === '1'

  const [connected, setConnected] = useState<boolean | null>(null)
  const [provider, setProvider] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [kiteBanner, setKiteBanner] = useState<{ kind: 'success' | 'error'; text: string } | null>(
    null,
  )

  const load = useCallback(async () => {
    try {
      const { data } = await api.get<BrokerStatusResponse>('/broker/status')
      setConnected(data.connected)
      setProvider(data.provider ?? null)
      setError(null)
    } catch {
      setError('Could not load broker status.')
      setConnected(null)
      setProvider(null)
    }
  }, [])

  useEffect(() => {
    const kite = searchParams.get('kite')
    const rawMsg = searchParams.get('message')
    if (kite === 'success') {
      setKiteBanner({ kind: 'success', text: 'Zerodha (Kite) connected successfully.' })
    } else if (kite === 'error') {
      const text = rawMsg
        ? decodeURIComponent(rawMsg.replace(/\+/g, ' '))
        : 'Zerodha login did not complete.'
      setKiteBanner({ kind: 'error', text })
    }

    if (kite) {
      const next = new URLSearchParams(searchParams)
      next.delete('kite')
      next.delete('message')
      navigate({ pathname: '/brokers', search: next.toString() ? `?${next}` : '' }, { replace: true })
    }

    void load()
  }, [navigate, searchParams, load])

  const completeSetup = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.post('/broker/complete-setup')
      await load()
      navigate('/', { replace: true })
    } catch (err) {
      setError(problemDetail(err))
    } finally {
      setBusy(false)
    }
  }

  const startKiteOAuth = async () => {
    setBusy(true)
    setError(null)
    try {
      const { data } = await api.get<{ loginUrl: string }>('/broker/kite/login-url')
      window.location.href = data.loginUrl
    } catch (err) {
      setError(problemDetail(err))
      setBusy(false)
    }
  }

  const isZerodha = provider?.toLowerCase() === 'zerodha'

  const disconnectBroker = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.post('/broker/disconnect')
      await load()
    } catch (err) {
      setError(problemDetail(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Layout>
      <h1 className="h3 mb-1">Broker connection</h1>
      <p className="text-secondary small mb-4" style={{ maxWidth: '42rem' }}>
        Trading features need a linked broker account. Complete this step once, then you can use the
        dashboard.
      </p>

      {setup ? (
        <Alert variant="warning" className="border border-warning">
          Finish connecting your broker below to open the dashboard.
        </Alert>
      ) : null}

      {kiteBanner ? (
        <Alert variant={kiteBanner.kind === 'success' ? 'success' : 'danger'} className="mt-3">
          {kiteBanner.text}
        </Alert>
      ) : null}

      {error ? (
        <Alert variant="danger" className="mt-3 mb-0">
          {error}
        </Alert>
      ) : null}

      <Card className="border-secondary mt-4">
        <Card.Body>
          <Card.Title className="h5">Link your broker</Card.Title>
          <Card.Text className="text-secondary small">
            Connect Zerodha via Kite Connect (OAuth). Your API keys and tokens stay on the server; the
            app only stores encrypted session tokens after login.
          </Card.Text>

          <Card.Text className="small mb-0">
            <span className="text-secondary">Status:</span>{' '}
            {connected === null ? (
              <span className="text-secondary">Loading…</span>
            ) : connected ? (
              <>
                <span className="text-success">Connected</span>
                {provider ? <span className="text-secondary"> ({provider})</span> : null}
              </>
            ) : (
              <span className="text-warning">Not connected</span>
            )}
          </Card.Text>

          {!connected ? (
            <>
              <Button
                variant="success"
                className="mt-4"
                disabled={busy || connected === null}
                onClick={() => void startKiteOAuth()}
              >
                {busy ? 'Opening Zerodha…' : 'Connect Zerodha (Kite)'}
              </Button>

              <hr className="border-secondary mt-4" />

              <p className="text-secondary small mb-3">
                For local development without Kite credentials, you can skip OAuth and mark this
                account as broker-ready (no live broker link).
              </p>
              <Button
                variant="outline-secondary"
                disabled={busy || connected === null}
                onClick={() => void completeSetup()}
              >
                {busy ? 'Saving…' : 'Skip — complete setup without broker'}
              </Button>
            </>
          ) : (
            <Stack direction="horizontal" gap={2} className="mt-4 flex-wrap">
              <Button variant="secondary" onClick={() => navigate('/', { replace: true })}>
                Go to dashboard
              </Button>
              {isZerodha ? (
                <>
                  <Button
                    variant="outline-secondary"
                    disabled={busy}
                    onClick={() => void startKiteOAuth()}
                  >
                    {busy ? 'Opening Zerodha…' : 'Reconnect Zerodha (Kite)'}
                  </Button>
                  <Button variant="outline-danger" disabled={busy} onClick={() => void disconnectBroker()}>
                    {busy ? 'Removing…' : 'Remove Zerodha connection'}
                  </Button>
                </>
              ) : (
                <>
                  <Button
                    variant="outline-secondary"
                    disabled={busy}
                    onClick={() => void startKiteOAuth()}
                  >
                    {busy ? 'Opening Zerodha…' : 'Connect Zerodha (Kite)'}
                  </Button>
                  <Button variant="outline-danger" disabled={busy} onClick={() => void disconnectBroker()}>
                    {busy ? 'Disconnecting…' : 'Disconnect broker'}
                  </Button>
                </>
              )}
            </Stack>
          )}
        </Card.Body>
      </Card>
    </Layout>
  )
}
