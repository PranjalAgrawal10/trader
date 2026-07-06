import { type FormEvent, useState } from 'react'
import { Button, ButtonGroup, Card, Col, Container, Form, Row, Alert, InputGroup } from 'react-bootstrap'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { navigateToAppAfterTwoFactor } from '../navigation/afterTwoFactor'
import { useAuthStore } from '../store/useAuthStore'
import { apiProblemDetail } from '../utils/apiProblemDetail'

function problemDetail(err: unknown, fallback: string): string {
  return apiProblemDetail(err, fallback)
}

type AuthPayload = {
  token: string
  userId?: string
  email: string
  role?: string
}

type TwoFactorChallenge = {
  requires_2fa: true
  temp_token: string
  second_factor: 'authenticator' | 'email_otp'
}

function isTwoFactorChallenge(x: unknown): x is TwoFactorChallenge {
  if (typeof x !== 'object' || x === null) return false
  const o = x as Record<string, unknown>
  const sf = o.second_factor
  return (
    o.requires_2fa === true &&
    typeof o.temp_token === 'string' &&
    (sf === 'authenticator' || sf === 'email_otp')
  )
}

function isRegisterAck(x: unknown): boolean {
  if (typeof x !== 'object' || x === null) return false
  return (x as Record<string, unknown>).email_verification_required === true
}

function isEmailVerificationGate(x: unknown): boolean {
  if (typeof x !== 'object' || x === null) return false
  return (x as Record<string, unknown>).requires_email_verification === true
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
  const [twoFactor, setTwoFactor] = useState<TwoFactorChallenge | null>(null)
  const [registerSent, setRegisterSent] = useState(false)
  const [totpCode, setTotpCode] = useState('')
  const [otpPasteState, setOtpPasteState] = useState<'idle' | 'ok' | 'error'>('idle')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const extractOtpDigits = (raw: string): string => {
    const digits = raw.replace(/\D/g, '')
    if (digits.length >= 6) return digits.slice(0, 6)
    return digits
  }

  const pasteEmailOtp = async () => {
    if (!twoFactor || twoFactor.second_factor !== 'email_otp') return
    try {
      if (!navigator.clipboard?.readText) throw new Error('Clipboard read is unavailable.')
      const text = await navigator.clipboard.readText()
      const otp = extractOtpDigits(text)
      if (otp.length === 0) throw new Error('No OTP digits found in clipboard.')
      setTotpCode(otp)
      setOtpPasteState('ok')
      setError(null)
    } catch {
      setOtpPasteState('error')
    } finally {
      window.setTimeout(() => setOtpPasteState('idle'), 1400)
    }
  }

  const continueAfterAuth = async (token: string, accountEmail: string) => {
    setAuth(token, accountEmail)
    try {
      const { data } = await api.get<{ two_factor_enabled: boolean }>('/2fa/status')
      if (!data.two_factor_enabled) {
        navigate('/profile?required=1', { replace: true })
        return
      }
    } catch {
      navigate('/profile?required=1', { replace: true })
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
        if (!isRegisterAck(data)) {
          setError('Unexpected response from server.')
          return
        }

        setRegisterSent(true)
        setPassword('')
        return
      }

      const { data } = await api.post<unknown>('/auth/login', {
        email,
        password,
      })

      if (isTwoFactorChallenge(data)) {
        setTwoFactor(data)
        setTotpCode('')
        return
      }

      if (isEmailVerificationGate(data)) {
        setError('Verify your email before signing in. Check your inbox for the link.')
        return
      }

      if (!isAuthPayload(data)) {
        setError('Invalid credentials.')
        return
      }

      await continueAfterAuth(data.token, data.email)
    } catch (err) {
      const msg =
        mode === 'login'
          ? apiProblemDetail(err, 'Invalid credentials.')
          : apiProblemDetail(err, 'Registration failed (email may already exist).')
      setError(msg)
    } finally {
      setBusy(false)
    }
  }

  const submitTotp = async (e: FormEvent) => {
    e.preventDefault()
    if (!twoFactor) return
    setError(null)
    setBusy(true)
    try {
      const { data } = await api.post<unknown>('/2fa/verify-login', {
        temp_token: twoFactor.temp_token,
        otp: totpCode.trim(),
      })
      if (!isAuthPayload(data)) {
        setError('Unexpected response from server.')
        return
      }
      setTwoFactor(null)
      setTotpCode('')
      await continueAfterAuth(data.token, data.email)
    } catch (err) {
      setError(problemDetail(err, 'Could not verify sign-in. Try again or use Back.'))
    } finally {
      setBusy(false)
    }
  }

  const resendLoginOtp = async () => {
    if (!twoFactor || twoFactor.second_factor !== 'email_otp') return
    setError(null)
    setBusy(true)
    try {
      await api.post('/auth/resend-login-otp', { temp_token: twoFactor.temp_token })
    } catch (err) {
      setError(problemDetail(err, 'Could not resend the code.'))
    } finally {
      setBusy(false)
    }
  }

  const backToPassword = () => {
    setTwoFactor(null)
    setTotpCode('')
    setOtpPasteState('idle')
    setError(null)
  }

  return (
    <Container fluid className="vh-100 d-flex align-items-center justify-content-center bg-body-tertiary px-3">
      <Row className="justify-content-center w-100">
        <Col xs={12} sm={10} md={6} lg={4}>
          <Card className="border-secondary shadow">
            <Card.Body className="p-4">
              <Card.Title className="text-center h4 mb-4">Trader Console</Card.Title>

              {twoFactor ? (
                <Form onSubmit={submitTotp}>
                  <p className="small text-secondary mb-3">
                    {twoFactor.second_factor === 'email_otp' ? (
                      <>
                        <strong>6-digit code</strong> sent to <strong>{email}</strong>. Timed out? Use <strong>Back</strong>.
                      </>
                    ) : (
                      <>
                        Authenticator or recovery code for <strong>{email}</strong>. Timed out? Use <strong>Back</strong>.
                      </>
                    )}
                  </p>
                  <Form.Group className="mb-3" controlId="login-totp">
                    <Form.Label className="small text-secondary text-uppercase">
                      {twoFactor.second_factor === 'email_otp' ? 'Code from email' : 'Authenticator or recovery code'}
                    </Form.Label>
                    {twoFactor.second_factor === 'email_otp' ? (
                      <InputGroup>
                        <Form.Control
                          inputMode="numeric"
                          autoComplete="one-time-code"
                          value={totpCode}
                          onChange={(ev) => setTotpCode(ev.target.value)}
                          placeholder="123456"
                          required
                          autoFocus
                        />
                        <Button variant="outline-secondary" type="button" onClick={() => void pasteEmailOtp()} disabled={busy}>
                          {otpPasteState === 'ok' ? 'Pasted' : otpPasteState === 'error' ? 'Paste failed' : 'Paste OTP'}
                        </Button>
                      </InputGroup>
                    ) : (
                      <Form.Control
                        inputMode="text"
                        autoComplete="one-time-code"
                        value={totpCode}
                        onChange={(ev) => setTotpCode(ev.target.value)}
                        placeholder="123456"
                        required
                        autoFocus
                      />
                    )}
                  </Form.Group>
                  {error ? (
                    <Form.Text className="text-danger d-block mb-3">{error}</Form.Text>
                  ) : null}
                  <Button variant="success" type="submit" className="w-100 mb-2" disabled={busy}>
                    {busy ? 'Please wait…' : 'Verify and sign in'}
                  </Button>
                  {twoFactor.second_factor === 'email_otp' ? (
                    <Button
                      variant="outline-secondary"
                      type="button"
                      className="w-100 mb-2"
                      disabled={busy}
                      onClick={() => void resendLoginOtp()}
                    >
                      Resend code
                    </Button>
                  ) : null}
                  <Button variant="outline-secondary" type="button" className="w-100" disabled={busy} onClick={backToPassword}>
                    Back
                  </Button>
                </Form>
              ) : registerSent && mode === 'register' ? (
                <div>
                  <Alert variant="success">
                    Verify email, then <strong>Login</strong>.
                  </Alert>
                  <Button variant="outline-secondary" className="w-100" onClick={() => setRegisterSent(false)}>
                    Back
                  </Button>
                </div>
              ) : (
                <>
                  <ButtonGroup className="w-100 mb-4">
                    <Button
                      variant={mode === 'login' ? 'success' : 'outline-secondary'}
                      onClick={() => {
                        setMode('login')
                        setError(null)
                        setRegisterSent(false)
                      }}
                    >
                      Login
                    </Button>
                    <Button
                      variant={mode === 'register' ? 'success' : 'outline-secondary'}
                      onClick={() => {
                        setMode('register')
                        setError(null)
                        setRegisterSent(false)
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
                    {mode === 'login' ? (
                      <div className="text-end mb-3">
                        <Link to="/forgot-password" className="small">
                          Forgot password?
                        </Link>
                      </div>
                    ) : null}
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
