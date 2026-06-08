import axios from 'axios'
import { useCallback, useEffect, useState } from 'react'
import { Alert, Badge, Button, ButtonGroup, Card, Form, InputGroup, Spinner, Stack } from 'react-bootstrap'
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

type BrokerProviderAvailability = {
  key: string
  label: string
  connected: boolean
}

const BROKER_PREF_STORAGE_KEY = 'trader-preferred-broker'

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
  const [providers, setProviders] = useState<BrokerProviderAvailability[]>([])
  const [selectedProvider, setSelectedProvider] = useState<string>(() => {
    const saved = window.localStorage.getItem(BROKER_PREF_STORAGE_KEY)?.trim().toLowerCase()
    return saved || 'zerodha'
  })
  const [kiteBanner, setKiteBanner] = useState<{ kind: 'success' | 'error'; text: string } | null>(
    null,
  )
  const [growwAccessToken, setGrowwAccessToken] = useState('')
  const [growwApiKey, setGrowwApiKey] = useState('')
  const [growwApiSecret, setGrowwApiSecret] = useState('')
  const [growwTotp, setGrowwTotp] = useState('')

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

  const loadProviders = useCallback(async () => {
    try {
      const { data } = await api.get<BrokerProviderAvailability[]>('/broker/providers')
      setProviders(Array.isArray(data) ? data : [])
    } catch {
      setProviders([])
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
    void loadProviders()
  }, [navigate, searchParams, loadBroker, loadProviders])

  useEffect(() => {
    const id = window.setInterval(() => {
      if (document.visibilityState === 'visible') {
        void loadBroker()
        void loadProviders()
      }
    }, 90_000)
    return () => window.clearInterval(id)
  }, [loadBroker, loadProviders])

  useEffect(() => {
    if (providers.length === 0) return
    if (providers.some((p) => p.key.toLowerCase() === selectedProvider)) return
    const fallback = providers[0]?.key?.toLowerCase()
    if (fallback) setSelectedProvider(fallback)
  }, [providers, selectedProvider])

  useEffect(() => {
    if (!selectedProvider) return
    window.localStorage.setItem(BROKER_PREF_STORAGE_KEY, selectedProvider)
  }, [selectedProvider])

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

  const connectGroww = async () => {
    if (!growwAccessToken.trim() && !growwApiKey.trim()) {
      setError('Provide Groww access token, or API key with secret/TOTP.')
      return
    }
    if (!growwAccessToken.trim() && growwApiKey.trim() && !growwApiSecret.trim() && !growwTotp.trim()) {
      setError('When using Groww API key flow, provide API secret or TOTP.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await api.post('/broker/groww/connect', {
        accessToken: growwAccessToken.trim() || undefined,
        apiKey: growwApiKey.trim() || undefined,
        apiSecret: growwApiSecret.trim() || undefined,
        totp: growwTotp.trim() || undefined,
      })
      setKiteBanner({ kind: 'success', text: 'Groww connected successfully.' })
      await loadBroker()
      await loadProviders()
      setGrowwAccessToken('')
      setGrowwApiSecret('')
      setGrowwTotp('')
    } catch (err) {
      setError(problemDetail(err))
    } finally {
      setBusy(false)
    }
  }

  const isZerodha = provider?.toLowerCase() === 'zerodha'
  const selectedProviderMeta = providers.find((p) => p.key.toLowerCase() === selectedProvider) ?? null
  const selectedIsConnected = selectedProviderMeta?.connected ?? false
  const selectedIsGroww = selectedProvider === 'groww'

  const disconnectBroker = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.post('/broker/disconnect', null, {
        params: selectedProvider ? { broker: selectedProvider } : undefined,
      })
      await loadBroker()
      await loadProviders()
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
            Choose your broker here and connect. Your API keys and tokens stay on the server; the app only stores
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
              {providers.length > 0 ? (
                <div className="mt-3">
                  <div className="small text-secondary mb-2">Broker options</div>
                  <ButtonGroup size="sm" className="flex-wrap">
                    {providers.map((p) => {
                      const key = p.key.toLowerCase()
                      const isActive = selectedProvider === key
                      return (
                        <Button
                          key={`broker-opt-${p.key}`}
                          type="button"
                          variant={isActive ? 'primary' : 'outline-secondary'}
                          onClick={() => setSelectedProvider(key)}
                        >
                          {p.label}{' '}
                          {p.connected ? <Badge bg="success" pill>connected</Badge> : null}
                        </Button>
                      )
                    })}
                  </ButtonGroup>
                </div>
              ) : null}
              {selectedIsGroww ? (
                <div className="mt-3">
                  <Alert variant="info" className="py-2 small mb-3">
                    Groww tokens expire daily. Use direct access token, or give API key with secret/TOTP for token generation.
                  </Alert>
                  <InputGroup className="mb-2">
                    <InputGroup.Text className="small">Access token</InputGroup.Text>
                    <Form.Control
                      value={growwAccessToken}
                      onChange={(e) => setGrowwAccessToken(e.target.value)}
                      placeholder="Paste Groww access token (optional if using key flow)"
                    />
                  </InputGroup>
                  <InputGroup className="mb-2">
                    <InputGroup.Text className="small">API key</InputGroup.Text>
                    <Form.Control
                      value={growwApiKey}
                      onChange={(e) => setGrowwApiKey(e.target.value)}
                      placeholder="Groww API key"
                    />
                  </InputGroup>
                  <InputGroup className="mb-2">
                    <InputGroup.Text className="small">API secret</InputGroup.Text>
                    <Form.Control
                      type="password"
                      value={growwApiSecret}
                      onChange={(e) => setGrowwApiSecret(e.target.value)}
                      placeholder="For approval flow"
                    />
                  </InputGroup>
                  <InputGroup>
                    <InputGroup.Text className="small">TOTP</InputGroup.Text>
                    <Form.Control
                      value={growwTotp}
                      onChange={(e) => setGrowwTotp(e.target.value)}
                      placeholder="Or 6-digit TOTP"
                    />
                  </InputGroup>
                </div>
              ) : null}

              {!connected ? (
                <>
                  <Button
                    variant="success"
                    className="mt-4"
                    disabled={busy || connected === null || gated}
                    onClick={() => {
                      if (selectedIsGroww) {
                        void connectGroww()
                        return
                      }
                      void startKiteOAuth()
                    }}
                  >
                    {selectedIsGroww ? (busy ? 'Connecting Groww…' : 'Connect Groww') : busy ? 'Opening Zerodha…' : 'Connect Zerodha (Kite)'}
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
                  {isZerodha || selectedIsConnected ? (
                    <>
                      <Button
                        variant="outline-secondary"
                        disabled={busy || gated}
                        onClick={() => {
                          if (selectedIsGroww) {
                            void connectGroww()
                            return
                          }
                          void startKiteOAuth()
                        }}
                      >
                        {selectedIsGroww ? (busy ? 'Connecting Groww…' : 'Reconnect Groww') : busy ? 'Opening Zerodha…' : 'Reconnect Zerodha (Kite)'}
                      </Button>
                      <Button variant="outline-danger" disabled={busy || gated} onClick={() => void disconnectBroker()}>
                        {busy ? 'Removing…' : selectedIsGroww ? 'Remove Groww connection' : 'Remove Zerodha connection'}
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
