import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useAuthStore } from '../store/useAuthStore'

function resolveApiOrigin(): string {
  const allowCrossOrigin = import.meta.env.VITE_FORCE_CROSS_ORIGIN_API === 'true'
  const raw = import.meta.env.VITE_API_BASE_URL?.trim()
  if (allowCrossOrigin && raw) return raw.replace(/\/$/, '')
  return ''
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
