import axios from 'axios'
import { useEffect, useState } from 'react'
import { Alert, Button, Card, Container, Spinner } from 'react-bootstrap'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { useAuthStore } from '../store/useAuthStore'

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)

  const [status, setStatus] = useState<'busy' | 'ok' | 'error'>('busy')
  const [message, setMessage] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const token = searchParams.get('token')
    void (async () => {
      if (!token || token.length < 16) {
        setStatus('error')
        setMessage('Invalid or missing token in link.')
        return
      }

      try {
        const { data } = await api.post<{ token: string; email: string }>('/auth/verify-email', { token })
        if (cancelled) return
        setAuth(data.token, data.email)
        setStatus('ok')
      } catch (err: unknown) {
        if (cancelled) return
        if (axios.isAxiosError(err)) {
          const detail = (err.response?.data as { detail?: string } | undefined)?.detail
          setMessage(detail ?? 'Verification failed.')
        } else setMessage('Verification failed.')

        setStatus('error')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [searchParams, setAuth])

  return (
    <Container className="py-5">
      <Card className="mx-auto" style={{ maxWidth: 420 }}>
        <Card.Body className="p-4">
          <Card.Title className="h5">Verify email</Card.Title>
          {status === 'busy' ? (
            <div className="d-flex align-items-center gap-2 text-secondary">
              <Spinner animation="border" size="sm" />
              Verifying your link…
            </div>
          ) : null}
          {status === 'ok' ? (
            <>
              <Alert variant="success" className="mb-3">
                Your email is verified. Continue to finish security setup if prompted.
              </Alert>
              <Button variant="success" className="w-100 mb-2" onClick={() => navigate('/profile?required=1', { replace: true })}>
                Continue to profile
              </Button>
              <Link className="d-block text-center small" to="/login">
                Back to sign in
              </Link>
            </>
          ) : null}
          {status === 'error' && message ? <Alert variant="danger">{message}</Alert> : null}
          {status === 'error' ? (
            <div className="mt-3 d-flex flex-column gap-2">
              <Link className="btn btn-outline-secondary" to="/login">
                Sign in
              </Link>
            </div>
          ) : null}
        </Card.Body>
      </Card>
    </Container>
  )
}
