import type { ReactElement } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { RequiresBroker } from './components/RequiresBroker'
import { RequiresTwoFactor } from './components/RequiresTwoFactor'
import { useAuthStore } from './store/useAuthStore'
import { BotsPage } from './pages/BotsPage'
import { BrokersPage } from './pages/BrokersPage'
import { DashboardPage } from './pages/DashboardPage'
import { KiteInstrumentsPage } from './pages/KiteInstrumentsPage'
import { LoginPage } from './pages/LoginPage'
import { StrategiesPage } from './pages/StrategiesPage'
import { SecurityPage } from './pages/SecurityPage'
import { TradesPage } from './pages/TradesPage'

function Protected({ children }: { children: ReactElement }) {
  const token = useAuthStore((s) => s.token)
  if (!token) return <Navigate to="/login" replace />
  return children
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/brokers"
        element={
          <Protected>
            <RequiresTwoFactor>
              <BrokersPage />
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
        path="/security"
        element={
          <Protected>
            <SecurityPage />
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
