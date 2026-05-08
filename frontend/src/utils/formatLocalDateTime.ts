export interface FormatLocalDateTimeOptions {
  /** Include millisecond fractional digits after the clock time (Intl `fractionalSecondDigits: 3`). */
  includeMilliseconds?: boolean
}

/**
 * Formats an API ISO timestamp for display in the browser's local timezone.
 * Use `includeMilliseconds` on charts when bar times need sub-second precision.
 */
export function formatLocalDateTime(
  value: string | number | Date | null | undefined,
  options?: FormatLocalDateTimeOptions,
): string {
  if (value == null || value === '') return '—'
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return typeof value === 'string' ? value : '—'
  if (options?.includeMilliseconds) {
    return d.toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      fractionalSecondDigits: 3,
    })
  }
  return d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}
