import { type FormEvent, useState } from 'react'
import { Alert, Button, Card, Container, Form, InputGroup } from 'react-bootstrap'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { apiProblemDetail } from '../utils/apiProblemDetail'

type Step = 'email' | 'reset'

export function ForgotPasswordPage() {
  const navigate = useNavigate()
  const [step, setStep] = useState<Step>('email')
  const [email, setEmail] = useState('')
  const [otp, setOtp] = useState('')
  const [password, setPassword] = useState('')
  const [repeat, setRepeat] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [otpPasteState, setOtpPasteState] = useState<'idle' | 'ok' | 'error'>('idle')

  const sendCode = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setInfo(null)
    const trimmed = email.trim()
    if (!trimmed) {
      setError('Enter your email address.')
      return
    }

    setBusy(true)
    try {
      await api.post('/auth/forgot-password', { email: trimmed })
      setStep('reset')
      setOtp('')
      setPassword('')
      setRepeat('')
      setInfo(
        'If an account exists for this email, a 6-digit code was sent (check spam). Wrong email? You will not receive a code.',
      )
    } catch (err: unknown) {
      setError(
        apiProblemDetail(
          err,
          'Could not send reset code. Email may be misconfigured on the server — contact support if this persists.',
        ),
      )
    } finally {
      setBusy(false)
    }
  }

  const resendCode = async () => {
    setError(null)
    setInfo(null)
    setBusy(true)
    try {
      await api.post('/auth/forgot-password', { email: email.trim() })
      setInfo('A new code was sent if an account exists for this email.')
    } catch (err: unknown) {
      setError(apiProblemDetail(err, 'Could not resend the code.'))
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
        setError('Clipboard does not contain a 6-digit code.')
      }
    } catch {
      setOtpPasteState('error')
      setError('Could not read from clipboard.')
    }
  }

  const resetPassword = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setInfo(null)

    const code = otp.replace(/\D/g, '')
    if (code.length !== 6) {
      setError('Enter the 6-digit code from your email.')
      return
    }
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
        otp: code,
        new_password: password,
      })
      navigate('/login', { replace: true })
    } catch (err: unknown) {
      setError(apiProblemDetail(err, 'Could not reset password.'))
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
    setInfo(null)
    setOtpPasteState('idle')
  }

  return (
    <Container className="py-5">
      <Card className="mx-auto" style={{ maxWidth: 420 }}>
        <Card.Body className="p-4">
          <Card.Title className="h5">Forgot password</Card.Title>

          {error ? (
            <Alert variant="danger" className="mt-3 mb-0" role="alert">
              {error}
            </Alert>
          ) : null}
          {info ? (
            <Alert variant="info" className="mt-3 mb-0">
              {info}
            </Alert>
          ) : null}

          {step === 'email' ? (
            <>
              <p className="small text-secondary mt-3">
                Enter your email and we will send a 6-digit reset code if an account exists.
              </p>
              <Form onSubmit={sendCode} className="mt-3">
                <Form.Group className="mb-3">
                  <Form.Label className="small text-secondary text-uppercase">Email</Form.Label>
                  <Form.Control
                    type="email"
                    value={email}
                    onChange={(ev) => setEmail(ev.target.value)}
                    required
                    autoComplete="email"
                    autoFocus
                    isInvalid={!!error && step === 'email'}
                  />
                </Form.Group>
                <Button variant="success" type="submit" className="w-100" disabled={busy}>
                  {busy ? 'Please wait…' : 'Send reset code'}
                </Button>
              </Form>
            </>
          ) : (
            <>
              <p className="small text-secondary mt-3 mb-0">
                Enter the <strong>6-digit code</strong> sent to <strong>{email}</strong>, then choose a new password.
              </p>
              <Form onSubmit={resetPassword} className="mt-3">
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
                      isInvalid={!!error && otp.length > 0}
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
