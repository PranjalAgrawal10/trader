import type { ReactElement } from 'react'
import { Navigate, Route, Routes, useSearchParams } from 'react-router-dom'
import { BROKER_PROFILE_SECTION_ID } from './constants/profileSections'
import { RequiresBroker } from './components/RequiresBroker'
import { RequiresTwoFactor } from './components/RequiresTwoFactor'
import { useAuthStore } from './store/useAuthStore'
import { BotsPage } from './pages/BotsPage'
import { DashboardPage } from './pages/DashboardPage'
import { KiteInstrumentsPage } from './pages/KiteInstrumentsPage'
import { LoginPage } from './pages/LoginPage'
import { ProfilePage } from './pages/ProfilePage'
import { VerifyEmailPage } from './pages/VerifyEmailPage'
import { ForgotPasswordPage } from './pages/ForgotPasswordPage'
import { ResetPasswordPage } from './pages/ResetPasswordPage'
import { StrategiesPage } from './pages/StrategiesPage'
import { TradesPage } from './pages/TradesPage'

function Protected({ children }: { children: ReactElement }) {
  const token = useAuthStore((s) => s.token)
  if (!token) return <Navigate to="/login" replace />
  return children
}

/** Preserves query string for old <code>/security</code> bookmarks. */
function SecurityToProfileRedirect() {
  const [searchParams] = useSearchParams()
  const q = searchParams.toString()
  return <Navigate to={q ? `/profile?${q}` : '/profile'} replace />
}

/** Preserves query (e.g. <code>?setup=1</code>) and scrolls broker section on old <code>/brokers</code> bookmarks. */
function BrokersToProfileRedirect() {
  const [searchParams] = useSearchParams()
  const q = searchParams.toString()
  const base = q ? `/profile?${q}` : '/profile'
  return <Navigate to={`${base}#${BROKER_PROFILE_SECTION_ID}`} replace />
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route
        path="/brokers"
        element={
          <Protected>
            <RequiresTwoFactor>
              <BrokersToProfileRedirect />
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route
        path="/instruments/automation"
        element={
          <Protected>
            <RequiresTwoFactor>
              <RequiresBroker>
                <Navigate to="/instruments?tab=automation" replace />
              </RequiresBroker>
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route
        path="/instruments"
        element={
          <Protected>
            <RequiresTwoFactor>
              <RequiresBroker>
                <KiteInstrumentsPage />
              </RequiresBroker>
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route
        path="/profile"
        element={
          <Protected>
            <ProfilePage />
          </Protected>
        }
      />
      <Route
        path="/security"
        element={
          <Protected>
            <SecurityToProfileRedirect />
          </Protected>
        }
      />
      <Route
        path="/"
        element={
          <Protected>
            <RequiresTwoFactor>
              <RequiresBroker>
                <DashboardPage />
              </RequiresBroker>
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route
        path="/strategies"
        element={
          <Protected>
            <RequiresTwoFactor>
              <RequiresBroker>
                <StrategiesPage />
              </RequiresBroker>
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route
        path="/bots"
        element={
          <Protected>
            <RequiresTwoFactor>
              <RequiresBroker>
                <BotsPage />
              </RequiresBroker>
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route
        path="/trades"
        element={
          <Protected>
            <RequiresTwoFactor>
              <RequiresBroker>
                <TradesPage />
              </RequiresBroker>
            </RequiresTwoFactor>
          </Protected>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
