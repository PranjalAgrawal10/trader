import { formatLocalDateTime } from '../utils/formatLocalDateTime'

export function HistoricalRangeCaption({
  candleInterval,
  fromIso,
  toIso,
  compact,
}: {
  candleInterval: string
  fromIso: string
  toIso: string
  compact?: boolean
}) {
  const from = formatLocalDateTime(fromIso)
  const to = formatLocalDateTime(toIso)
  return (
    <div className={compact ? 'small text-secondary mb-2' : 'small text-secondary mb-3'}>
      <div className={compact ? 'mb-0' : 'mb-1'}>
        <strong className="text-body-secondary">From</strong>{' '}
        <span className="font-monospace">{from}</span>
        <span className="text-body-secondary"> - </span>
        <strong className="text-body-secondary">To</strong>{' '}
        <span className="font-monospace">{to}</span>
      </div>
      <div className="text-muted" style={{ fontSize: compact ? '0.72rem' : '0.78rem' }}>
        Candle interval: {candleInterval}
      </div>
    </div>
  )
}
