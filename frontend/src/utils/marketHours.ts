const IST_TIME_ZONE = 'Asia/Kolkata'
const IST_PARTS = new Intl.DateTimeFormat('en-GB', {
  timeZone: IST_TIME_ZONE,
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

const IST_MARKET_START_MIN = 9 * 60 + 15
const IST_MARKET_PREOPEN_MIN = IST_MARKET_START_MIN - 5
const IST_MARKET_END_MIN = 15 * 60 + 30
const IST_WEEKDAYS = new Set(['Mon', 'Tue', 'Wed', 'Thu', 'Fri'])

/**
 * NSE/BSE intraday live-pull window in IST.
 * Active from 5 minutes before market open (09:10) until market close (15:30), weekdays only.
 */
export function isIstMarketLiveWindow(now: Date = new Date()): boolean {
  const parts = IST_PARTS.formatToParts(now)
  const byType = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? ''
  const weekday = byType('weekday')
  if (!IST_WEEKDAYS.has(weekday)) return false
  const hour = Number.parseInt(byType('hour'), 10)
  const minute = Number.parseInt(byType('minute'), 10)
  if (!Number.isFinite(hour) || !Number.isFinite(minute)) return false
  const minutes = hour * 60 + minute
  return minutes >= IST_MARKET_PREOPEN_MIN && minutes <= IST_MARKET_END_MIN
}
