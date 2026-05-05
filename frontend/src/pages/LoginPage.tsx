import axios from 'axios'
import { type FormEvent, useState } from 'react'
import { Button, ButtonGroup, Card, Col, Container, Form, Row } from 'react-bootstrap'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { navigateToAppAfterTwoFactor } from '../navigation/afterTwoFactor'
import { useAuthStore } from '../store/useAuthStore'

function problemDetail(err: unknown): string | null {
  if (axios.isAxiosError(err)) {
    const body = err.response?.data as { detail?: string; title?: string } | undefined
    const s = body?.detail ?? body?.title ?? (err.response?.status === 401 ? err.message : null)
    return s && s.length > 0 ? s : null
  }
  return null
}

type AuthPayload = {
  token: string
  userId: string
  email: string
  role: string
}

function isTwoFactorChallenge(x: unknown): x is { requires_2fa: true; temp_token: string } {
  if (typeof x !== 'object' || x === null) return false
  const o = x as Record<string, unknown>
  return o.requires_2fa === true && typeof o.temp_token === 'string'
}

function isAuthPayload(x: unknown): x is AuthPayload {
  if (typeof x !== 'object' || x === null) return false
  const o = x as Record<string, unknown>
  return typeof o.token === 'string' && typeof o.email === 'string'
}

export function LoginPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [twoFactorToken, setTwoFactorToken] = useState<string | null>(null)
  const [totpCode, setTotpCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const continueAfterAuth = async (token: string, accountEmail: string) => {
    setAuth(token, accountEmail)
    try {
      const { data } = await api.get<{ two_factor_enabled: boolean }>('/2fa/status')
      if (!data.two_factor_enabled) {
        navigate('/security?required=1', { replace: true })
        return
      }
    } catch {
      navigate('/security?required=1', { replace: true })
      return
    }
    await navigateToAppAfterTwoFactor(navigate)
  }

  const submitPassword = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      if (mode === 'register') {
        const { data } = await api.post<unknown>('/auth/register', {
          email,
          password,
        })
        if (!isAuthPayload(data)) {
          setError('Unexpected response from server.')
          return
        }
        await continueAfterAuth(data.token, data.email)
        return
      }

      const { data } = await api.post<unknown>('/auth/login', {
        email,
        password,
      })

      if (isTwoFactorChallenge(data)) {
        setTwoFactorToken(data.temp_token)
        setTotpCode('')
        return
      }

      if (!isAuthPayload(data)) {
        setError('Invalid credentials.')
        return
      }

      await continueAfterAuth(data.token, data.email)
    } catch {
      const msg =
        mode === 'login'
          ? 'Invalid credentials.'
          : 'Registration failed (email may already exist).'
      setError(msg)
    } finally {
      setBusy(false)
    }
  }

  const submitTotp = async (e: FormEvent) => {
    e.preventDefault()
    if (!twoFactorToken) return
    setError(null)
    setBusy(true)
    try {
      const { data } = await api.post<unknown>('/2fa/verify-login', {
        temp_token: twoFactorToken,
        otp: totpCode.trim(),
      })
      if (!isAuthPayload(data)) {
        setError('Unexpected response from server.')
        return
      }
      setTwoFactorToken(null)
      setTotpCode('')
      await continueAfterAuth(data.token, data.email)
    } catch (err) {
      setError(problemDetail(err) ?? 'Could not verify sign-in. Try again or use Back.')
    } finally {
      setBusy(false)
    }
  }

  const backToPassword = () => {
    setTwoFactorToken(null)
    setTotpCode('')
    setError(null)
  }

  return (
    <Container fluid className="vh-100 d-flex align-items-center justify-content-center bg-body-tertiary px-3">
      <Row className="justify-content-center w-100">
        <Col xs={12} sm={10} md={6} lg={4}>
          <Card className="border-secondary shadow">
            <Card.Body className="p-4">
              <Card.Title className="text-center h4 mb-2">Trader Console</Card.Title>
              <Card.Text className="text-center text-secondary small mb-4">
                Sign in to manage strategies and bots.
              </Card.Text>

              {twoFactorToken ? (
                <Form onSubmit={submitTotp}>
                  <p className="small text-secondary mb-3">
                    Enter your 6-digit authenticator code, or an unused recovery code, for <strong>{email}</strong>. If you
                    waited a long time after entering your password, this step may time out — use <strong>Back</strong> and
                    sign in again.
                  </p>
                  <Form.Group className="mb-3" controlId="login-totp">
                    <Form.Label className="small text-secondary text-uppercase">Authenticator or recovery code</Form.Label>
                    <Form.Control
                      inputMode="text"
                      autoComplete="one-time-code"
                      value={totpCode}
                      onChange={(ev) => setTotpCode(ev.target.value)}
                      placeholder="123456"
                      required
                      autoFocus
                    />
                  </Form.Group>
                  {error ? (
                    <Form.Text className="text-danger d-block mb-3">{error}</Form.Text>
                  ) : null}
                  <Button variant="success" type="submit" className="w-100 mb-2" disabled={busy}>
                    {busy ? 'Please wait…' : 'Verify and sign in'}
                  </Button>
                  <Button variant="outline-secondary" type="button" className="w-100" disabled={busy} onClick={backToPassword}>
                    Back
                  </Button>
                </Form>
              ) : (
                <>
                  <ButtonGroup className="w-100 mb-4">
                    <Button
                      variant={mode === 'login' ? 'success' : 'outline-secondary'}
                      onClick={() => {
                        setMode('login')
                        setError(null)
                      }}
                    >
                      Login
                    </Button>
                    <Button
                      variant={mode === 'register' ? 'success' : 'outline-secondary'}
                      onClick={() => {
                        setMode('register')
                        setError(null)
                      }}
                    >
                      Register
                    </Button>
                  </ButtonGroup>

                  <Form onSubmit={submitPassword}>
                    <Form.Group className="mb-3" controlId="login-email">
                      <Form.Label className="small text-secondary text-uppercase">Email</Form.Label>
                      <Form.Control
                        type="email"
                        autoComplete="email"
                        value={email}
                        onChange={(ev) => setEmail(ev.target.value)}
                        required
                      />
                    </Form.Group>
                    <Form.Group className="mb-3" controlId="login-password">
                      <Form.Label className="small text-secondary text-uppercase">Password</Form.Label>
                      <Form.Control
                        type="password"
                        autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                        value={password}
                        onChange={(ev) => setPassword(ev.target.value)}
                        required
                        minLength={6}
                      />
                    </Form.Group>
                    {error ? (
                      <Form.Text className="text-danger d-block mb-3">{error}</Form.Text>
                    ) : null}
                    <Button variant="success" type="submit" className="w-100" disabled={busy}>
                      {busy ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account'}
                    </Button>
                  </Form>
                </>
              )}
            </Card.Body>
          </Card>
        </Col>
      </Row>
    </Container>
  )
}
