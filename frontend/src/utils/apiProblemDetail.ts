import axios from 'axios'

type ProblemBody = {
  detail?: string
  title?: string
  status?: number
}

/** Extract a user-facing message from an API error (ASP.NET ProblemDetails). */
export function apiProblemDetail(err: unknown, fallback: string): string {
  if (!axios.isAxiosError(err)) return fallback

  const body = err.response?.data as ProblemBody | undefined
  const fromBody = body?.detail?.trim() || body?.title?.trim()
  if (fromBody) return fromBody

  if (err.response?.status === 429) return 'Too many attempts. Wait a moment and try again.'
  if (err.code === 'ERR_NETWORK') return 'Network error — check your connection and try again.'

  return fallback
}
