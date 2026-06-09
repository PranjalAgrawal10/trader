import { HubConnectionBuilder, HubConnectionState, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { useAuthStore } from '../store/useAuthStore'
import { resolveSpaApiBaseOrigin } from '../api/sameOrigin'

/** Payload items match backend MarketTickDto (camelCase JSON). */
export type MarketTickBatchItem = { i: number; p: number; v: number; t?: number | null }

type MarketHubTransportMode = 'websocket' | 'auto'

function buildMarketHubConnection(mode: MarketHubTransportMode) {
  const hubUrl = `${resolveSpaApiBaseOrigin()}/hubs/market`
  const conn = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => useAuthStore.getState().token ?? '',
      ...(mode === 'websocket'
        ? {
            transport: HttpTransportType.WebSockets,
            skipNegotiation: true,
          }
        : {}),
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
  ;(conn as { __marketMode?: MarketHubTransportMode }).__marketMode = mode
  return conn
}

export function createMarketHubConnection() {
  // WebSocket-first for lowest chart latency; fallback to auto transport if WS is blocked.
  return buildMarketHubConnection('websocket')
}

export async function startMarketHub(conn: ReturnType<typeof createMarketHubConnection>): Promise<ReturnType<typeof createMarketHubConnection>> {
  if (conn.state === HubConnectionState.Connected) return conn
  try {
    await conn.start()
    return conn
  } catch (err) {
    const mode = (conn as { __marketMode?: MarketHubTransportMode }).__marketMode
    if (mode !== 'websocket')
      throw err

    try {
      await conn.stop()
    } catch {
      /* ignore */
    }

    const fallback = buildMarketHubConnection('auto')
    await fallback.start()
    return fallback
  }
}
