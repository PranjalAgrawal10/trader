import type { ReactElement } from 'react'
import { useEffect, useState } from 'react'
import { Container, Spinner } from 'react-bootstrap'
import { Navigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuthStore } from '../store/useAuthStore'

interface TwoFactorStatus {
  two_factor_enabled: boolean
}

/** Until TOTP 2FA is enabled, only `/profile` (and login) are reachable with a session. */
export function RequiresTwoFactor({ children }: { children: ReactElement }) {
  const token = useAuthStore((s) => s.token)
  const [gate, setGate] = useState<'loading' | 'ok' | 'need'>('loading')

  useEffect(() => {
    if (!token) {
      setGate('loading')
      return
    }

    let cancelled = false
    ;(async () => {
      try {
        const { data } = await api.get<TwoFactorStatus>('/2fa/status')
        if (!cancelled) setGate(data.two_factor_enabled ? 'ok' : 'need')
      } catch {
        if (!cancelled) setGate('need')
      }
    })()

    return () => {
      cancelled = true
    }
  }, [token])

  if (!token) return <Navigate to="/login" replace />

  if (gate === 'loading') {
    return (
      <Container
        fluid
        className="vh-100 d-flex align-items-center justify-content-center bg-body-tertiary text-secondary"
      >
        <Spinner animation="border" size="sm" className="me-2" role="status" aria-hidden />
        Checking security settings…
      </Container>
    )
  }

  if (gate === 'need') return <Navigate to="/profile?required=1" replace />

  return children
}
