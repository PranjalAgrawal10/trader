function pad2(n: number): string {
  return String(n).padStart(2, '0')
}

function pad3(n: number): string {
  return String(n).padStart(3, '0')
}

/**
 * Formats an API ISO timestamp (or epoch ms) for display in the browser's **local** timezone.
 *
 * **Canonical SPA shape:** `DD/MM/YY HH.mm.ss.fff` — two-digit day/month/year, 24-hour clock,
 * dot-separated time parts, three-digit milliseconds.
 */
export function formatLocalDateTime(value: string | number | Date | null | undefined): string {
  if (value == null || value === '') return '—'
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return typeof value === 'string' ? value : '—'

  const dd = pad2(d.getDate())
  const mm = pad2(d.getMonth() + 1)
  const yy = pad2(d.getFullYear() % 100)
  const HH = pad2(d.getHours())
  const min = pad2(d.getMinutes())
  const ss = pad2(d.getSeconds())
  const fff = pad3(d.getMilliseconds())

  return `${dd}/${mm}/${yy} ${HH}.${min}.${ss}.${fff}`
}
