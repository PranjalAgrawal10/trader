import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useAuthStore } from '../store/useAuthStore'
import { resolveSpaApiBaseOrigin } from '../api/sameOrigin'

/** Payload items match backend MarketTickDto (camelCase JSON). */
export type MarketTickBatchItem = { i: number; p: number; v: number; t?: number | null }

export function createMarketHubConnection() {
  // Same-origin by default so the SignalR negotiate POST does not trigger CORS preflight OPTIONS.
  const hubUrl = `${resolveSpaApiBaseOrigin()}/hubs/market`
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
