import type { NavigateFunction } from 'react-router-dom'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { api } from '../api/client'

/** After the account has 2FA enabled, send the user to the dashboard or broker onboarding (profile). */
export async function navigateToAppAfterTwoFactor(navigate: NavigateFunction) {
  try {
    const { data } = await api.get<{ connected: boolean }>('/broker/status')
    navigate(
      data.connected ? '/' : `/profile?setup=1#${BROKER_PROFILE_SECTION_ID}`,
      { replace: true },
    )
  } catch {
    navigate(`/profile?setup=1#${BROKER_PROFILE_SECTION_ID}`, { replace: true })
  }
}
