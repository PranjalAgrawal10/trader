import { Navigate, Route, Routes } from 'react-router-dom'
import {
  ForgotPasswordPage,
  KiteInstrumentsPage,
  LoginPage,
  ProfilePage,
  ResetPasswordPage,
  ScalperPage,
  TradesPage,
  VerifyEmailPage,
  WalletPage,
} from './lazyPages'
import {
  BrokerRoute,
  BrokersToProfileRedirect,
  ProtectedRoute,
  SecurityToProfileRedirect,
  TwoFactorRoute,
} from './RouteGuards'

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />

      <Route
        path="/brokers"
        element={
          <TwoFactorRoute>
            <BrokersToProfileRedirect />
          </TwoFactorRoute>
        }
      />
      <Route
        path="/security"
        element={
          <ProtectedRoute>
            <SecurityToProfileRedirect />
          </ProtectedRoute>
        }
      />
      <Route
        path="/profile"
        element={
          <ProtectedRoute>
            <ProfilePage />
          </ProtectedRoute>
        }
      />
      <Route
        path="/wallet"
        element={
          <TwoFactorRoute>
            <WalletPage />
          </TwoFactorRoute>
        }
      />

      <Route
        path="/instruments/manual-trade"
        element={
          <BrokerRoute>
            <Navigate to="/instruments?tab=manual-trade" replace />
          </BrokerRoute>
        }
      />
      <Route
        path="/instruments/locked"
        element={
          <BrokerRoute>
            <Navigate to="/instruments?tab=locked" replace />
          </BrokerRoute>
        }
      />
      <Route
        path="/instruments"
        element={
          <BrokerRoute>
            <KiteInstrumentsPage />
          </BrokerRoute>
        }
      />

      <Route
        path="/"
        element={
          <BrokerRoute>
            <Navigate to="/instruments" replace />
          </BrokerRoute>
        }
      />
      <Route
        path="/strategies"
        element={
          <BrokerRoute>
            <Navigate to="/instruments" replace />
          </BrokerRoute>
        }
      />
      <Route
        path="/bots"
        element={
          <BrokerRoute>
            <Navigate to="/instruments" replace />
          </BrokerRoute>
        }
      />
      <Route
        path="/trades"
        element={
          <BrokerRoute>
            <TradesPage />
          </BrokerRoute>
        }
      />
      <Route
        path="/scalper"
        element={
          <BrokerRoute>
            <ScalperPage />
          </BrokerRoute>
        }
      />

      <Route path="*" element={<Navigate to="/instruments" replace />} />
    </Routes>
  )
}
