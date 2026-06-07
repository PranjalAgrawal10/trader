import { lazy, Suspense, type ReactElement } from 'react'
import { Navigate, Route, Routes, useSearchParams } from 'react-router-dom'
import { BROKER_PROFILE_SECTION_ID } from './constants/profileSections'
import { RequiresBroker } from './components/RequiresBroker'
import { RequiresTwoFactor } from './components/RequiresTwoFactor'
import { useAuthStore } from './store/useAuthStore'

const KiteInstrumentsPage = lazy(() => import('./pages/KiteInstrumentsPage').then((m) => ({ default: m.KiteInstrumentsPage })))
const LoginPage = lazy(() => import('./pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const ProfilePage = lazy(() => import('./pages/ProfilePage').then((m) => ({ default: m.ProfilePage })))
const VerifyEmailPage = lazy(() => import('./pages/VerifyEmailPage').then((m) => ({ default: m.VerifyEmailPage })))
const ForgotPasswordPage = lazy(() => import('./pages/ForgotPasswordPage').then((m) => ({ default: m.ForgotPasswordPage })))
const ResetPasswordPage = lazy(() => import('./pages/ResetPasswordPage').then((m) => ({ default: m.ResetPasswordPage })))
const TradesPage = lazy(() => import('./pages/TradesPage').then((m) => ({ default: m.TradesPage })))
const ScalperPage = lazy(() => import('./pages/ScalperPage').then((m) => ({ default: m.ScalperPage })))
const WalletPage = lazy(() => import('./pages/WalletPage').then((m) => ({ default: m.WalletPage })))

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
    <Suspense fallback={<div className="p-3 small text-secondary">Loading…</div>}>
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
          path="/instruments/manual-trade"
          element={
            <Protected>
              <RequiresTwoFactor>
                <RequiresBroker>
                  <Navigate to="/instruments?tab=manual-trade" replace />
                </RequiresBroker>
              </RequiresTwoFactor>
            </Protected>
          }
        />
        <Route
          path="/instruments/locked"
          element={
            <Protected>
              <RequiresTwoFactor>
                <RequiresBroker>
                  <Navigate to="/instruments?tab=locked" replace />
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
          path="/wallet"
          element={
            <Protected>
              <RequiresTwoFactor>
                <WalletPage />
              </RequiresTwoFactor>
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
                  <Navigate to="/instruments" replace />
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
                  <Navigate to="/instruments" replace />
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
                  <Navigate to="/instruments" replace />
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
        <Route
          path="/scalper"
          element={
            <Protected>
              <RequiresTwoFactor>
                <RequiresBroker>
                  <ScalperPage />
                </RequiresBroker>
              </RequiresTwoFactor>
            </Protected>
          }
        />
        <Route path="*" element={<Navigate to="/instruments" replace />} />
      </Routes>
    </Suspense>
  )
}
