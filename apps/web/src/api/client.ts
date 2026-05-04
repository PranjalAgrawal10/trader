import axios from 'axios'
import { useAuthStore } from '../store/useAuthStore'

function resolveApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_BASE_URL
  if (!raw?.trim()) {
    throw new Error('VITE_API_BASE_URL is missing. Add it to apps/web/.env.development or .env.production.')
  }
  return raw.replace(/\/$/, '')
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
