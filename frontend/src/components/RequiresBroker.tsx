import type { ReactElement } from 'react'
import { useEffect, useState } from 'react'
import { Container, Spinner } from 'react-bootstrap'
import { Navigate } from 'react-router-dom'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { api } from '../api/client'
import { useAuthStore } from '../store/useAuthStore'

interface BrokerStatus {
  connected: boolean
}

/** Sends users to Profile (broker section) until broker onboarding is complete. */
export function RequiresBroker({ children }: { children: ReactElement }) {
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
        const { data } = await api.get<BrokerStatus>('/broker/status')
        if (!cancelled) setGate(data.connected ? 'ok' : 'need')
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
        Checking broker connection…
      </Container>
    )
  }

  if (gate === 'need')
    return <Navigate to={`/profile?setup=1#${BROKER_PROFILE_SECTION_ID}`} replace />

  return children
}
