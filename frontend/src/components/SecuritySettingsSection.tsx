import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { Alert, Button, Card, Form, Spinner } from 'react-bootstrap'
import QRCode from 'react-qr-code'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { navigateToAppAfterTwoFactor } from '../navigation/afterTwoFactor'

type TwoFactorStatus = {
  two_factor_enabled: boolean
  enrollment_pending: boolean
}

type EnrollmentBegin = {
  manual_entry_key: string
  otp_auth_uri: string
}

type Props = {
  /** From <code>?required=1</code> when 2FA gate sends the user here. */
  setupRequired: boolean
}

export function SecuritySettingsSection({ setupRequired }: Props) {
  const navigate = useNavigate()

  const [status, setStatus] = useState<TwoFactorStatus | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const [enrollment, setEnrollment] = useState<EnrollmentBegin | null>(null)
  const [confirmCode, setConfirmCode] = useState('')
  const [disablePassword, setDisablePassword] = useState('')
  const [disableCode, setDisableCode] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [lastRecoveryCodes, setLastRecoveryCodes] = useState<string[] | null>(null)

  const refreshStatus = useCallback(async () => {
    setLoadError(null)
    try {
      const { data } = await api.get<TwoFactorStatus>('/2fa/status')
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
      const { data } = await api.post<EnrollmentBegin>('/2fa/setup')
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
      const { data } = await api.post<{ recovery_codes?: string[] }>('/2fa/verify-setup', {
        otp: confirmCode.trim(),
      })
      setEnrollment(null)
      setConfirmCode('')
      setLastRecoveryCodes(data.recovery_codes ?? [])
      await refreshStatus()
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
      await api.post('/2fa/cancel-setup')
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
    const pwd = disablePassword.trim()
    const otp = disableCode.trim()
    if (!pwd && !otp) {
      setFormError('Enter your current password, or your authenticator / recovery code.')
      return
    }
    setMessage(null)
    setFormError(null)
    setBusy(true)
    try {
      const body: { password?: string; otp?: string } = {}
      if (pwd.length > 0) body.password = pwd
      if (otp.length > 0) body.otp = otp
      await api.post('/2fa/disable', body)
      setDisablePassword('')
      setDisableCode('')
      setMessage('Two-factor authentication has been turned off.')
      await refreshStatus()
    } catch {
      setFormError('Could not disable. Check your password or authenticator / recovery code.')
    } finally {
      setBusy(false)
    }
  }

  if (loadError) {
    return <Alert variant="danger">{loadError}</Alert>
  }

  if (!status) {
    return (
      <div className="d-flex align-items-center gap-2 text-secondary">
        <Spinner animation="border" size="sm" />
        Loading security settings…
      </div>
    )
  }

  return (
    <section id="security-2fa" aria-label="Two-factor authentication">
      {setupRequired && !status.two_factor_enabled ? (
        <Alert variant="warning">
          <strong>Authenticator setup required.</strong> Add your account to an authenticator app and confirm a code before
          you can use the rest of the console.
        </Alert>
      ) : null}
      {message ? <Alert variant="success">{message}</Alert> : null}
      {formError ? <Alert variant="danger">{formError}</Alert> : null}
      {lastRecoveryCodes?.length ? (
        <Alert variant="info" className="font-monospace small">
          <strong>Recovery codes — save these now.</strong> Each code works once. They are not shown again.
          <ul className="mb-0 mt-2">
            {lastRecoveryCodes.map((c) => (
              <li key={c}>{c}</li>
            ))}
          </ul>
          <div className="mt-3 d-flex flex-wrap gap-2">
            <Button
              variant="outline-secondary"
              size="sm"
              type="button"
              disabled={busy}
              onClick={() => void navigator.clipboard.writeText(lastRecoveryCodes.join('\n'))}
            >
              Copy all
            </Button>
            <Button
              variant="success"
              size="sm"
              type="button"
              disabled={busy}
              onClick={() => {
                setBusy(true)
                setLastRecoveryCodes(null)
                void navigateToAppAfterTwoFactor(navigate).finally(() => setBusy(false))
              }}
            >
              Continue to dashboard
            </Button>
          </div>
        </Alert>
      ) : null}

      <Card className="border-secondary shadow-sm mb-4">
        <Card.Body>
          <Card.Title className="h6">Authenticator app (TOTP)</Card.Title>
          <Card.Text className="text-secondary small">
            Use an app such as Google Authenticator or Microsoft Authenticator. Time-based codes: 6 digits, 30 seconds,
            SHA-1 (±30s drift allowed).
          </Card.Text>

          {status.two_factor_enabled ? (
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
                    placeholder="Or leave blank if using a code only"
                  />
                </Form.Group>
                <Form.Group className="mb-3" controlId="disable-code">
                  <Form.Label className="small text-secondary text-uppercase">Authenticator or recovery code</Form.Label>
                  <Form.Control
                    inputMode="text"
                    autoComplete="one-time-code"
                    value={disableCode}
                    onChange={(ev) => setDisableCode(ev.target.value)}
                    placeholder="Either password or a code — not necessarily both"
                  />
                </Form.Group>
                <Button variant="outline-danger" type="submit" disabled={busy}>
                  Turn off 2FA
                </Button>
              </Form>
            </div>
          ) : enrollment || status.enrollment_pending ? (
            <div>
              {!enrollment && status.enrollment_pending ? (
                <p className="small text-secondary mb-3">
                  {setupRequired
                    ? 'You have a setup in progress. Generate a new QR code to continue.'
                    : 'You have a setup in progress. Generate a new QR code to continue, or cancel below.'}
                </p>
              ) : null}
              {enrollment ? (
                <>
                  <p className="small mb-3">Scan this QR code with your authenticator app, or enter the key manually.</p>
                  <div className="bg-white p-3 rounded border mb-3 d-inline-block">
                    <QRCode value={enrollment.otp_auth_uri} size={200} />
                  </div>
                  <Form.Group className="mb-3">
                    <Form.Label className="small text-secondary text-uppercase">Manual entry key</Form.Label>
                    <Form.Control readOnly value={enrollment.manual_entry_key} className="font-monospace" />
                  </Form.Group>
                  <Form onSubmit={confirmEnrollment} className="mb-3">
                    <Form.Group className="mb-3" controlId="confirm-totp">
                      <Form.Label className="small text-secondary text-uppercase">Enter code from the app to confirm</Form.Label>
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
    </section>
  )
}
