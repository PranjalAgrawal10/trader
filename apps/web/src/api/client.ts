import axios from 'axios'
import { useAuthStore } from '../store/useAuthStore'

function resolveApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL?.trim()
  if (raw)
    return raw.replace(/\/$/, '')

  // Production on same host as API (e.g. App Platform: `/api` → API, `/` → static): Vite may not have
  // BUILD_TIME env; using the page origin avoids a blank bundle from a missing `VITE_API_BASE_URL`.
  if (!import.meta.env.DEV && typeof window !== 'undefined' && window.location?.origin)
    return window.location.origin.replace(/\/$/, '')

  throw new Error(
    'VITE_API_BASE_URL is missing. Add it to apps/web/.env.development (local) or .env.production / App Platform BUILD_TIME env.',
  )
}

export const api = axios.create({
  baseURL: `${resolveApiBaseUrl()}/api/v1`,
  withCredentials: true,
})

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})
