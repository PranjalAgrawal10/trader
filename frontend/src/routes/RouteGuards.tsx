import type { ReactElement } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { RequiresBroker } from '../components/RequiresBroker'
import { RequiresTwoFactor } from '../components/RequiresTwoFactor'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { useAuthStore } from '../store/useAuthStore'

export function ProtectedRoute({ children }: { children: ReactElement }) {
  const token = useAuthStore((s) => s.token)
  if (!token) return <Navigate to="/login" replace />
  return children
}

export function TwoFactorRoute({ children }: { children: ReactElement }) {
  return (
    <ProtectedRoute>
      <RequiresTwoFactor>{children}</RequiresTwoFactor>
    </ProtectedRoute>
  )
}

export function BrokerRoute({ children }: { children: ReactElement }) {
  return (
    <TwoFactorRoute>
      <RequiresBroker>{children}</RequiresBroker>
    </TwoFactorRoute>
  )
}

/** Preserves query string for old <code>/security</code> bookmarks. */
export function SecurityToProfileRedirect() {
  const [searchParams] = useSearchParams()
  const q = searchParams.toString()
  return <Navigate to={q ? `/profile?${q}` : '/profile'} replace />
}

/** Preserves query (e.g. <code>?setup=1</code>) and scrolls broker section on old <code>/brokers</code> bookmarks. */
export function BrokersToProfileRedirect() {
  const [searchParams] = useSearchParams()
  const q = searchParams.toString()
  const base = q ? `/profile?${q}` : '/profile'
  return <Navigate to={`${base}#${BROKER_PROFILE_SECTION_ID}`} replace />
}
