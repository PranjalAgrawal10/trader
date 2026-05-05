import axios from 'axios'
import { useAuthStore } from '../store/useAuthStore'

/** Relative to axios `baseURL` (`.../api/v1`). Omit Bearer so an expired stored JWT does not fail JWT validation before login/register. */
const pathsWithoutBearer = new Set([
  'auth/login',
  'auth/register',
  'auth/verify-email',
  'auth/forgot-password',
  'auth/reset-password',
  'auth/resend-login-otp',
  'auth/email-otp/send',
  'auth/email-otp/verify',
  '2fa/verify-login',
])

function relativePath(url: string | undefined): string {
  if (!url) return ''
  const withoutQuery = url.split('?')[0]
  return withoutQuery.startsWith('/') ? withoutQuery.slice(1) : withoutQuery
}

function resolveApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL?.trim()
  if (raw)
    return raw.replace(/\/$/, '')

  // Production on same host as API (e.g. App Platform: `/api` → API, `/` → static): Vite may not have
  // BUILD_TIME env; using the page origin avoids a blank bundle from a missing `VITE_API_BASE_URL`.
  if (!import.meta.env.DEV && typeof window !== 'undefined' && window.location?.origin)
    return window.location.origin.replace(/\/$/, '')

  throw new Error(
    'VITE_API_BASE_URL is missing. Add it to frontend/.env.development (local) or .env.production / App Platform BUILD_TIME env.',
  )
}

export const api = axios.create({
  baseURL: `${resolveApiBaseUrl()}/api/v1`,
  withCredentials: true,
})

api.interceptors.request.use((config) => {
  const path = relativePath(config.url)
  if (path && !pathsWithoutBearer.has(path)) {
    const token = useAuthStore.getState().token
    if (token) config.headers.Authorization = `Bearer ${token}`
  } else {
    delete config.headers.Authorization
  }
  return config
})

api.interceptors.response.use(
  (r) => r,
  (err) => {
    if (!axios.isAxiosError(err) || err.response?.status !== 401) return Promise.reject(err)

    const path = relativePath(err.config?.url)
    if (path === 'auth/login') return Promise.reject(err)

    const hadToken = !!useAuthStore.getState().token
    useAuthStore.getState().logout()

    if (hadToken && typeof window !== 'undefined' && !window.location.pathname.endsWith('/login')) {
      window.location.assign('/login')
    }
    return Promise.reject(err)
  },
)
