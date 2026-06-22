import { lazy } from 'react'

export const KiteInstrumentsPage = lazy(() =>
  import('../pages/KiteInstrumentsPage').then((m) => ({ default: m.KiteInstrumentsPage })),
)
export const LoginPage = lazy(() => import('../pages/LoginPage').then((m) => ({ default: m.LoginPage })))
export const ProfilePage = lazy(() => import('../pages/ProfilePage').then((m) => ({ default: m.ProfilePage })))
export const VerifyEmailPage = lazy(() =>
  import('../pages/VerifyEmailPage').then((m) => ({ default: m.VerifyEmailPage })),
)
export const ForgotPasswordPage = lazy(() =>
  import('../pages/ForgotPasswordPage').then((m) => ({ default: m.ForgotPasswordPage })),
)
export const ResetPasswordPage = lazy(() =>
  import('../pages/ResetPasswordPage').then((m) => ({ default: m.ResetPasswordPage })),
)
export const TradesPage = lazy(() => import('../pages/TradesPage').then((m) => ({ default: m.TradesPage })))
export const ScalperPage = lazy(() => import('../pages/ScalperPage').then((m) => ({ default: m.ScalperPage })))
export const WalletPage = lazy(() => import('../pages/WalletPage').then((m) => ({ default: m.WalletPage })))
