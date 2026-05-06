import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useAuthStore } from '../store/useAuthStore'

function resolveApiOrigin(): string {
  const raw = import.meta.env.VITE_API_BASE_URL?.trim()
  if (raw) return raw.replace(/\/$/, '')
  if (!import.meta.env.DEV && typeof window !== 'undefined' && window.location?.origin) {
    return window.location.origin.replace(/\/$/, '')
  }
  throw new Error(
    'VITE_API_BASE_URL is missing. Add it to frontend/.env.development (local) so the market hub can reach the API.',
  )
}

/** Payload items match backend MarketTickDto (camelCase JSON). */
export type MarketTickBatchItem = { i: number; p: number; v: number; t?: number | null }

export function createMarketHubConnection() {
  const hubUrl = `${resolveApiOrigin()}/hubs/market`
  return new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => useAuthStore.getState().token ?? '',
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
}

export async function startMarketHub(conn: ReturnType<typeof createMarketHubConnection>): Promise<void> {
  if (conn.state === HubConnectionState.Connected) return
  await conn.start()
}
