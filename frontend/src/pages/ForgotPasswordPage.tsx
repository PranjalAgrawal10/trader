import axios from 'axios'
import { type FormEvent, useState } from 'react'
import { Alert, Button, Card, Container, Form, InputGroup } from 'react-bootstrap'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'

type Step = 'email' | 'reset'

function problemDetail(err: unknown): string | null {
  if (!axios.isAxiosError(err)) return null
  return (err.response?.data as { detail?: string } | undefined)?.detail ?? null
}

export function ForgotPasswordPage() {
  const navigate = useNavigate()
  const [step, setStep] = useState<Step>('email')
  const [email, setEmail] = useState('')
  const [otp, setOtp] = useState('')
  const [password, setPassword] = useState('')
  const [repeat, setRepeat] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [otpPasteState, setOtpPasteState] = useState<'idle' | 'ok' | 'error'>('idle')

  const sendCode = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await api.post('/auth/forgot-password', { email: email.trim() })
      setStep('reset')
      setOtp('')
      setPassword('')
      setRepeat('')
    } catch (err: unknown) {
      setError(problemDetail(err) ?? 'Could not send reset code. Try again later.')
    } finally {
      setBusy(false)
    }
  }

  const resendCode = async () => {
    setError(null)
    setBusy(true)
    try {
      await api.post('/auth/forgot-password', { email: email.trim() })
    } catch (err: unknown) {
      setError(problemDetail(err) ?? 'Could not resend the code.')
    } finally {
      setBusy(false)
    }
  }

  const pasteOtp = async () => {
    setOtpPasteState('idle')
    try {
      const text = await navigator.clipboard.readText()
      const digits = text.replace(/\D/g, '').slice(0, 6)
      if (digits.length === 6) {
        setOtp(digits)
        setOtpPasteState('ok')
      } else {
        setOtpPasteState('error')
      }
    } catch {
      setOtpPasteState('error')
    }
  }

  const resetPassword = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)

    if (password.length < 6) {
      setError('Use at least 6 characters.')
      return
    }
    if (password !== repeat) {
      setError('Passwords do not match.')
      return
    }

    setBusy(true)
    try {
      await api.post('/auth/reset-password', {
        email: email.trim(),
        otp: otp.trim(),
        new_password: password,
      })
      navigate('/login', { replace: true })
    } catch (err: unknown) {
      setError(problemDetail(err) ?? 'Could not reset password.')
    } finally {
      setBusy(false)
    }
  }

  const backToEmail = () => {
    setStep('email')
    setOtp('')
    setPassword('')
    setRepeat('')
    setError(null)
    setOtpPasteState('idle')
  }

  return (
    <Container className="py-5">
      <Card className="mx-auto" style={{ maxWidth: 420 }}>
        <Card.Body className="p-4">
          <Card.Title className="h5">Forgot password</Card.Title>

          {step === 'email' ? (
            <>
              <p className="small text-secondary">
                Enter your email and we will send a 6-digit reset code if an account exists.
              </p>
              <Form onSubmit={sendCode}>
                {error ? <Alert variant="danger">{error}</Alert> : null}
                <Form.Group className="mb-3">
                  <Form.Label className="small text-secondary text-uppercase">Email</Form.Label>
                  <Form.Control
                    type="email"
                    value={email}
                    onChange={(ev) => setEmail(ev.target.value)}
                    required
                    autoComplete="email"
                    autoFocus
                  />
                </Form.Group>
                <Button variant="success" type="submit" className="w-100" disabled={busy}>
                  {busy ? 'Please wait…' : 'Send reset code'}
                </Button>
              </Form>
            </>
          ) : (
            <>
              <p className="small text-secondary mb-3">
                Enter the <strong>6-digit code</strong> sent to <strong>{email}</strong>, then choose a new password.
                Check spam. No code after a minute? Confirm the email matches your account or tap <strong>Resend code</strong>.
              </p>
              <Form onSubmit={resetPassword}>
                {error ? <Alert variant="danger">{error}</Alert> : null}
                <Form.Group className="mb-3">
                  <Form.Label className="small text-secondary text-uppercase">Code from email</Form.Label>
                  <InputGroup>
                    <Form.Control
                      inputMode="numeric"
                      autoComplete="one-time-code"
                      value={otp}
                      onChange={(ev) => setOtp(ev.target.value)}
                      placeholder="123456"
                      required
                      autoFocus
                    />
                    <Button variant="outline-secondary" type="button" onClick={() => void pasteOtp()} disabled={busy}>
                      {otpPasteState === 'ok' ? 'Pasted' : otpPasteState === 'error' ? 'Paste failed' : 'Paste OTP'}
                    </Button>
                  </InputGroup>
                </Form.Group>
                <Form.Group className="mb-3">
                  <Form.Label className="small text-secondary text-uppercase">New password</Form.Label>
                  <Form.Control
                    type="password"
                    autoComplete="new-password"
                    value={password}
                    onChange={(ev) => setPassword(ev.target.value)}
                    minLength={6}
                    required
                  />
                </Form.Group>
                <Form.Group className="mb-3">
                  <Form.Label className="small text-secondary text-uppercase">Confirm password</Form.Label>
                  <Form.Control
                    type="password"
                    autoComplete="new-password"
                    value={repeat}
                    onChange={(ev) => setRepeat(ev.target.value)}
                    minLength={6}
                    required
                  />
                </Form.Group>
                <Button variant="success" type="submit" className="w-100 mb-2" disabled={busy}>
                  {busy ? 'Please wait…' : 'Update password'}
                </Button>
                <Button
                  variant="outline-secondary"
                  type="button"
                  className="w-100 mb-2"
                  disabled={busy}
                  onClick={() => void resendCode()}
                >
                  Resend code
                </Button>
                <Button variant="outline-secondary" type="button" className="w-100" disabled={busy} onClick={backToEmail}>
                  Back
                </Button>
              </Form>
            </>
          )}

          <Link className="d-block mt-3 text-center small" to="/login">
            Back to sign in
          </Link>
        </Card.Body>
      </Card>
    </Container>
  )
}
