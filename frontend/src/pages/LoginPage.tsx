import { type FormEvent, useState } from 'react'
import { Button, ButtonGroup, Card, Col, Container, Form, Row } from 'react-bootstrap'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuthStore } from '../store/useAuthStore'

export function LoginPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const path = mode === 'login' ? '/auth/login' : '/auth/register'
      const { data } = await api.post<{ token: string; email: string }>(path, {
        email,
        password,
      })
      setAuth(data.token, data.email)
      try {
        const status = await api.get<{ connected: boolean }>('/broker/status')
        navigate(status.data.connected ? '/' : '/brokers?setup=1', { replace: true })
      } catch {
        navigate('/brokers?setup=1', { replace: true })
      }
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

              <ButtonGroup className="w-100 mb-4">
                <Button
                  variant={mode === 'login' ? 'success' : 'outline-secondary'}
                  onClick={() => setMode('login')}
                >
                  Login
                </Button>
                <Button
                  variant={mode === 'register' ? 'success' : 'outline-secondary'}
                  onClick={() => setMode('register')}
                >
                  Register
                </Button>
              </ButtonGroup>

              <Form onSubmit={submit}>
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
            </Card.Body>
          </Card>
        </Col>
      </Row>
    </Container>
  )
}
