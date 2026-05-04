import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Form, Row, Spinner } from 'react-bootstrap'
import QRCode from 'react-qr-code'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { Layout } from '../components/Layout'
import { navigateToAppAfterTwoFactor } from '../navigation/afterTwoFactor'

type TwoFactorStatus = {
  twoFactorEnabled: boolean
  enrollmentPending: boolean
}

type EnrollmentBegin = {
  manualEntryKey: string
  otpAuthUri: string
}

export function SecurityPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const setupRequired = searchParams.get('required') === '1'

  const [status, setStatus] = useState<TwoFactorStatus | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [enrollment, setEnrollment] = useState<EnrollmentBegin | null>(null)
  const [confirmCode, setConfirmCode] = useState('')
  const [disablePassword, setDisablePassword] = useState('')
  const [disableCode, setDisableCode] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)

  const refreshStatus = useCallback(async () => {
    setLoadError(null)
    try {
      const { data } = await api.get<TwoFactorStatus>('/auth/2fa/status')
      setStatus(data)
    } catch {
      setLoadError('Could not load security settings.')
    }
  }, [])

  useEffect(() => {
    void refreshStatus()
  }, [refreshStatus])

  const beginEnrollment = async () => {
    setMessage(null)
    setFormError(null)
    setBusy(true)
    try {
      const { data } = await api.post<EnrollmentBegin>('/auth/2fa/enrollment/begin')
      setEnrollment(data)
      setConfirmCode('')
      await refreshStatus()
    } catch {
      setFormError('Could not start authenticator setup.')
    } finally {
      setBusy(false)
    }
  }

  const confirmEnrollment = async (e: FormEvent) => {
    e.preventDefault()
    setMessage(null)
    setFormError(null)
    setBusy(true)
    try {
      await api.post('/auth/2fa/enrollment/confirm', { code: confirmCode.trim() })
      setEnrollment(null)
      setConfirmCode('')
      await refreshStatus()
      await navigateToAppAfterTwoFactor(navigate)
    } catch {
      setFormError('Invalid code or setup expired. Try again from the start.')
    } finally {
      setBusy(false)
    }
  }

  const cancelEnrollment = async () => {
    setFormError(null)
    setBusy(true)
    try {
      await api.post('/auth/2fa/enrollment/cancel')
      setEnrollment(null)
      setConfirmCode('')
      await refreshStatus()
    } catch {
      setFormError('Could not cancel setup.')
    } finally {
      setBusy(false)
    }
  }

  const disable2fa = async (e: FormEvent) => {
    e.preventDefault()
    setMessage(null)
    setFormError(null)
    setBusy(true)
    try {
      await api.post('/auth/2fa/disable', {
        password: disablePassword,
        code: disableCode.trim(),
      })
      setDisablePassword('')
      setDisableCode('')
      setMessage('Two-factor authentication has been turned off.')
      await refreshStatus()
    } catch {
      setFormError('Could not disable. Check password and authenticator code.')
    } finally {
      setBusy(false)
    }
  }

  if (loadError) {
    return (
      <Layout>
        <Alert variant="danger">{loadError}</Alert>
      </Layout>
    )
  }

  if (!status) {
    return (
      <Layout>
        <div className="d-flex align-items-center gap-2 text-secondary">
          <Spinner animation="border" size="sm" />
          Loading…
        </div>
      </Layout>
    )
  }

  return (
    <Layout>
    <Row className="justify-content-center">
      <Col xs={12} md={10} lg={8}>
        <h1 className="h4 mb-3">Security</h1>
        {setupRequired && !status.twoFactorEnabled ? (
          <Alert variant="warning">
            <strong>Authenticator setup required.</strong> Add your account to an authenticator app and confirm a code
            before you can use the rest of the console.
          </Alert>
        ) : null}
        {message ? <Alert variant="success">{message}</Alert> : null}
        {formError ? <Alert variant="danger">{formError}</Alert> : null}

        <Card className="border-secondary shadow-sm mb-4">
          <Card.Body>
            <Card.Title className="h6">Authenticator app (TOTP)</Card.Title>
            <Card.Text className="text-secondary small">
              Use an app such as Google Authenticator or Microsoft Authenticator. Standard time-based one-time
              passwords: 6 digits, 30 seconds, SHA-1.
            </Card.Text>

            {status.twoFactorEnabled ? (
              <div>
                <p className="text-success small mb-3">Two-factor authentication is on for this account.</p>
                <Form onSubmit={disable2fa}>
                  <Form.Group className="mb-3" controlId="disable-password">
                    <Form.Label className="small text-secondary text-uppercase">Current password</Form.Label>
                    <Form.Control
                      type="password"
                      autoComplete="current-password"
                      value={disablePassword}
                      onChange={(ev) => setDisablePassword(ev.target.value)}
                      required
                    />
                  </Form.Group>
                  <Form.Group className="mb-3" controlId="disable-code">
                    <Form.Label className="small text-secondary text-uppercase">Authenticator code</Form.Label>
                    <Form.Control
                      inputMode="numeric"
                      autoComplete="one-time-code"
                      value={disableCode}
                      onChange={(ev) => setDisableCode(ev.target.value)}
                      placeholder="123456"
                      required
                    />
                  </Form.Group>
                  <Button variant="outline-danger" type="submit" disabled={busy}>
                    Turn off 2FA
                  </Button>
                </Form>
              </div>
            ) : enrollment || status.enrollmentPending ? (
              <div>
                {!enrollment && status.enrollmentPending ? (
                  <p className="small text-secondary mb-3">
                    {setupRequired
                      ? 'You have a setup in progress. Generate a new QR code to continue.'
                      : 'You have a setup in progress. Generate a new QR code to continue, or cancel below.'}
                  </p>
                ) : null}
                {enrollment ? (
                  <>
                    <p className="small mb-3">
                      Scan this QR code with your authenticator app, or enter the key manually.
                    </p>
                    <div className="bg-white p-3 rounded border mb-3 d-inline-block">
                      <QRCode value={enrollment.otpAuthUri} size={200} />
                    </div>
                    <Form.Group className="mb-3">
                      <Form.Label className="small text-secondary text-uppercase">Manual entry key</Form.Label>
                      <Form.Control readOnly value={enrollment.manualEntryKey} className="font-monospace" />
                    </Form.Group>
                    <Form onSubmit={confirmEnrollment} className="mb-3">
                      <Form.Group className="mb-3" controlId="confirm-totp">
                        <Form.Label className="small text-secondary text-uppercase">
                          Enter code from the app to confirm
                        </Form.Label>
                        <Form.Control
                          inputMode="numeric"
                          autoComplete="one-time-code"
                          value={confirmCode}
                          onChange={(ev) => setConfirmCode(ev.target.value)}
                          placeholder="123456"
                          required
                        />
                      </Form.Group>
                      <div className="d-flex flex-wrap gap-2">
                        <Button variant="success" type="submit" disabled={busy}>
                          Confirm and enable
                        </Button>
                        {!setupRequired ? (
                          <Button variant="outline-secondary" type="button" disabled={busy} onClick={cancelEnrollment}>
                            Cancel setup
                          </Button>
                        ) : null}
                      </div>
                    </Form>
                  </>
                ) : (
                  <div className="d-flex flex-wrap gap-2">
                    <Button variant="success" disabled={busy} onClick={beginEnrollment}>
                      Continue setup (show QR code)
                    </Button>
                    {!setupRequired ? (
                      <Button variant="outline-secondary" disabled={busy} onClick={cancelEnrollment}>
                        Cancel setup
                      </Button>
                    ) : null}
                  </div>
                )}
              </div>
            ) : (
              <Button variant="success" disabled={busy} onClick={beginEnrollment}>
                Set up authenticator app
              </Button>
            )}
          </Card.Body>
        </Card>
      </Col>
    </Row>
    </Layout>
  )
}
