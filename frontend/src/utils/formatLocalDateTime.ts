function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

function pad3(n: number): string {
  return String(n).padStart(3, '0')
}

const IST_TIME_ZONE = 'Asia/Kolkata'
const IST_DATE_TIME_FORMATTER = new Intl.DateTimeFormat('en-GB', {
  timeZone: IST_TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
  fractionalSecondDigits: 3,
})

/**
 * Formats an API ISO timestamp (or epoch ms) in Indian time (Asia/Kolkata).
 *
 * **Canonical SPA shape:** `DD/MM/YY HH.mm.ss.fff` — two-digit day/month/year, 24-hour clock,
 * dot-separated time parts, three-digit milliseconds.
 */
export function formatLocalDateTime(value: string | number | Date | null | undefined): string {
  if (value == null || value === '') return '—'
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return typeof value === 'string' ? value : '—'

  const parts = IST_DATE_TIME_FORMATTER.formatToParts(d)
  const byType = (t: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === t)?.value ?? ''

  const dd = byType('day') || pad2(d.getDate())
  const mm = byType('month') || pad2(d.getMonth() + 1)
  const yy = byType('year') || pad2(d.getFullYear() % 100)
  const HH = byType('hour') || pad2(d.getHours())
  const min = byType('minute') || pad2(d.getMinutes())
  const ss = byType('second') || pad2(d.getSeconds())
  const fff = byType('fractionalSecond') || pad3(d.getMilliseconds())

  return `${dd}/${mm}/${yy} ${HH}.${min}.${ss}.${fff}`
}
