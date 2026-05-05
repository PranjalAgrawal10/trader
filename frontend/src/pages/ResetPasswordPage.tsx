import axios from 'axios'
import { type FormEvent, useState } from 'react'
import { Alert, Button, Card, Container, Form } from 'react-bootstrap'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()

  const [password, setPassword] = useState('')
  const [repeat, setRepeat] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    const token = searchParams.get('token')
    if (!token) {
      setError('Missing token in URL.')
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
        token,
        new_password: password,
      })
      navigate('/login', { replace: true })
    } catch (err: unknown) {
      if (axios.isAxiosError(err)) {
        const detail = (err.response?.data as { detail?: string } | undefined)?.detail
        setError(detail ?? 'Could not reset password.')
      } else setError('Could not reset password.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Container className="py-5">
      <Card className="mx-auto" style={{ maxWidth: 420 }}>
        <Card.Body className="p-4">
          <Card.Title className="h5">Choose a new password</Card.Title>
          <Form onSubmit={submit}>
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
            {error ? <Alert variant="danger">{error}</Alert> : null}
            <Button variant="success" type="submit" className="w-100" disabled={busy}>
              Update password
            </Button>
          </Form>
          <Link className="d-block mt-3 text-center small" to="/login">
            Back to sign in
          </Link>
        </Card.Body>
      </Card>
    </Container>
  )
}
