import { useEffect, useState } from 'react'
import { Table } from 'react-bootstrap'
import { api } from '../api/client'
import { Layout } from '../components/Layout'

interface TradeRow {
  id: string
  botId: string
  symbol: string
  side: number | string
  quantity: number
  price: number
  realizedPnl: number | null
  executedAt: string
}

export function TradesPage() {
  const [rows, setRows] = useState<TradeRow[]>([])
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api
      .get<TradeRow[]>('/trades')
      .then((r) => setRows(r.data))
      .catch(() => setError('Failed to load trades.'))
  }, [])

  const sideLabel = (s: number | string) => {
    const v = typeof s === 'string' ? s.toLowerCase() : s
    if (v === 'sell' || v === 1) return 'Sell'
    return 'Buy'
  }

  return (
    <Layout>
      <h1 className="h3 mb-1">Trade history</h1>
      <p className="text-secondary small mb-4">Filtered server-side to the latest window.</p>

      {error ? <p className="text-danger small mb-4">{error}</p> : null}

      <div className="rounded border border-secondary overflow-x-auto">
        <Table responsive hover className="mb-0 small">
          <thead className="table-dark">
            <tr>
              <th>Time</th>
              <th>Symbol</th>
              <th>Side</th>
              <th>Qty</th>
              <th>Price</th>
              <th>P&L</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((t) => (
              <tr key={t.id}>
                <td className="text-secondary text-nowrap">
                  {new Date(t.executedAt).toLocaleString()}
                </td>
                <td className="fw-medium">{t.symbol}</td>
                <td>{sideLabel(t.side)}</td>
                <td>{t.quantity}</td>
                <td>{t.price}</td>
                <td>{t.realizedPnl ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </Table>
      </div>
    </Layout>
  )
}
