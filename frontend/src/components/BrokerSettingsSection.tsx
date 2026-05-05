import axios from 'axios'
import { useCallback, useEffect, useState } from 'react'
import { Alert, Button, Card, Spinner, Stack } from 'react-bootstrap'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'

interface BrokerStatusResponse {
  connected: boolean
  connectedAt: string | null
  provider: string | null
}

type TwoFactorGate = {
  two_factor_enabled: boolean
}

function problemDetail(err: unknown): string {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    return body?.detail ?? body?.title ?? err.message ?? 'Request failed.'
  }
  return 'Request failed.'
}

type Props = {
  /** From <code>?setup=1</code> when the app sends users here before opening the dashboard. */
  brokerSetupRequired: boolean
  /** Increment to re-check 2FA prerequisite (e.g. after enabling TOTP on this page). */
  twoFaEpoch?: number
}

/** Zerodha / Kite connection UI — same endpoints as former <code>/brokers</code> page. */
export function BrokerSettingsSection({ brokerSetupRequired, twoFaEpoch = 0 }: Props) {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  const [twoFaOk, setTwoFaOk] = useState<boolean | null>(null)
  const [connected, setConnected] = useState<boolean | null>(null)
  const [provider, setProvider] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [kiteBanner, setKiteBanner] = useState<{ kind: 'success' | 'error'; text: string } | null>(
    null,
  )

  const loadBroker = useCallback(async () => {
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

  const loadTwoFa = useCallback(async () => {
    try {
      const { data } = await api.get<TwoFactorGate>('/2fa/status')
      setTwoFaOk(data.two_factor_enabled)
    } catch {
      setTwoFaOk(false)
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
      const search = next.toString() ? `?${next.toString()}` : ''
      navigate(
        { pathname: '/profile', search, hash: `#${BROKER_PROFILE_SECTION_ID}` },
        { replace: true },
      )
      return
    }

    void loadBroker()
  }, [navigate, searchParams, loadBroker])

  useEffect(() => {
    void loadTwoFa()
  }, [loadTwoFa, twoFaEpoch])

  const completeSetup = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.post('/broker/complete-setup')
      await loadBroker()
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
      await loadBroker()
    } catch (err) {
      setError(problemDetail(err))
    } finally {
      setBusy(false)
    }
  }

  const gated = twoFaOk === false

  return (
    <section id={BROKER_PROFILE_SECTION_ID} aria-label="Broker connection">
      <p className="text-secondary small mb-3" style={{ maxWidth: '42rem' }}>
        Trading features need a linked broker account. Tokens stay on the server; the browser only carries your login
        session.
      </p>

      {brokerSetupRequired ? (
        <Alert variant="warning" className="border border-warning">
          Finish connecting your broker below to open the dashboard.
        </Alert>
      ) : null}

      {gated ? (
        <Alert variant="secondary" className="mb-3">
          <strong>Set up two-factor authentication first.</strong> Use the Security section above, then return here to
          link Zerodha.
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
          <Card.Title className="h6">Link your broker</Card.Title>
          <Card.Text className="text-secondary small">
            Connect Zerodha via Kite Connect (OAuth). Your API keys and tokens stay on the server; the app only stores
            encrypted session tokens after login.
          </Card.Text>

          {twoFaOk === null ? (
            <div className="d-flex align-items-center gap-2 text-secondary mt-3">
              <Spinner animation="border" size="sm" />
              Checking prerequisites…
            </div>
          ) : (
            <>
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
                    disabled={busy || connected === null || gated}
                    onClick={() => void startKiteOAuth()}
                  >
                    {busy ? 'Opening Zerodha…' : 'Connect Zerodha (Kite)'}
                  </Button>

                  <hr className="border-secondary mt-4" />

                  <p className="text-secondary small mb-3">
                    For local development without Kite credentials, you can skip OAuth and mark this account as
                    broker-ready (no live broker link).
                  </p>
                  <Button
                    variant="outline-secondary"
                    disabled={busy || connected === null || gated}
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
                        disabled={busy || gated}
                        onClick={() => void startKiteOAuth()}
                      >
                        {busy ? 'Opening Zerodha…' : 'Reconnect Zerodha (Kite)'}
                      </Button>
                      <Button variant="outline-danger" disabled={busy || gated} onClick={() => void disconnectBroker()}>
                        {busy ? 'Removing…' : 'Remove Zerodha connection'}
                      </Button>
                    </>
                  ) : (
                    <>
                      <Button
                        variant="outline-secondary"
                        disabled={busy || gated}
                        onClick={() => void startKiteOAuth()}
                      >
                        {busy ? 'Opening Zerodha…' : 'Connect Zerodha (Kite)'}
                      </Button>
                      <Button variant="outline-danger" disabled={busy || gated} onClick={() => void disconnectBroker()}>
                        {busy ? 'Disconnecting…' : 'Disconnect broker'}
                      </Button>
                    </>
                  )}
                </Stack>
              )}
            </>
          )}
        </Card.Body>
      </Card>
    </section>
  )
}
