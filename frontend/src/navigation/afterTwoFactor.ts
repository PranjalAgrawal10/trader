import type { NavigateFunction } from 'react-router-dom'
import { BROKER_PROFILE_SECTION_ID } from '../constants/profileSections'
import { api } from '../api/client'

/** After 2FA, send the user to instruments or broker onboarding (profile). */
export async function navigateToAppAfterTwoFactor(navigate: NavigateFunction) {
  try {
    const { data } = await api.get<{ connected: boolean }>('/broker/status')
    navigate(
      data.connected ? '/instruments' : `/profile?setup=1#${BROKER_PROFILE_SECTION_ID}`,
      { replace: true },
    )
  } catch {
    navigate(`/profile?setup=1#${BROKER_PROFILE_SECTION_ID}`, { replace: true })
  }
}
