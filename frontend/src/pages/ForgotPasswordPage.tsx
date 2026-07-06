import axios from 'axios'
import { type FormEvent, useState } from 'react'
import { Alert, Button, Card, Container, Form } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import { api } from '../api/client'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await api.post('/auth/forgot-password', { email: email.trim() })
      setDone(true)
    } catch (err: unknown) {
      if (axios.isAxiosError(err)) {
        const detail = (err.response?.data as { detail?: string } | undefined)?.detail
        setError(detail ?? 'Could not send reset email. Try again later.')
      } else {
        setError('Could not send reset email. Try again later.')
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <Container className="py-5">
      <Card className="mx-auto" style={{ maxWidth: 420 }}>
        <Card.Body className="p-4">
          <Card.Title className="h5">Forgot password</Card.Title>
          <p className="small text-secondary">
            If an account exists for this email, we will send a reset link shortly.
          </p>
          {done ? (
            <Alert variant="info">Check your inbox for instructions (and spam).</Alert>
          ) : (
            <Form onSubmit={submit}>
              {error ? <Alert variant="danger">{error}</Alert> : null}
              <Form.Group className="mb-3">
                <Form.Label className="small text-secondary text-uppercase">Email</Form.Label>
                <Form.Control
                  type="email"
                  value={email}
                  onChange={(ev) => setEmail(ev.target.value)}
                  required
                  autoComplete="email"
                />
              </Form.Group>
              <Button variant="success" type="submit" className="w-100" disabled={busy}>
                Send reset link
              </Button>
            </Form>
          )}
          <Link className="d-block mt-3 text-center small" to="/login">
            Back to sign in
          </Link>
        </Card.Body>
      </Card>
    </Container>
  )
}
