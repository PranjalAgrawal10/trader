import type { ReactElement } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { RequiresBroker } from './components/RequiresBroker'
import { useAuthStore } from './store/useAuthStore'
import { BotsPage } from './pages/BotsPage'
import { BrokersPage } from './pages/BrokersPage'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { StrategiesPage } from './pages/StrategiesPage'
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
            <BrokersPage />
          </Protected>
        }
      />
      <Route
        path="/"
        element={
          <Protected>
            <RequiresBroker>
              <DashboardPage />
            </RequiresBroker>
          </Protected>
        }
      />
      <Route
        path="/strategies"
        element={
          <Protected>
            <RequiresBroker>
              <StrategiesPage />
            </RequiresBroker>
          </Protected>
        }
      />
      <Route
        path="/bots"
        element={
          <Protected>
            <RequiresBroker>
              <BotsPage />
            </RequiresBroker>
          </Protected>
        }
      />
      <Route
        path="/trades"
        element={
          <Protected>
            <RequiresBroker>
              <TradesPage />
            </RequiresBroker>
          </Protected>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
