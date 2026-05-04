import type { NavigateFunction } from 'react-router-dom'
import { api } from '../api/client'

/** After the account has 2FA enabled, send the user to the dashboard or broker onboarding. */
export async function navigateToAppAfterTwoFactor(navigate: NavigateFunction) {
  try {
    const { data } = await api.get<{ connected: boolean }>('/broker/status')
    navigate(data.connected ? '/' : '/brokers?setup=1', { replace: true })
  } catch {
    navigate('/brokers?setup=1', { replace: true })
  }
}
