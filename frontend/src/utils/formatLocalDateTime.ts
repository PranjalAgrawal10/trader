/**
 * Formats an API ISO timestamp for display in the browser's local timezone.
 */
export function formatLocalDateTime(value: string | number | Date | null | undefined): string {
  if (value == null || value === '') return '—'
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return typeof value === 'string' ? value : '—'
  return d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}
