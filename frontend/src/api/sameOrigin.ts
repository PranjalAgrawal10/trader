/**
 * Resolve the base origin used by the API client and the SignalR hub.
 *
 * Goals (in priority order):
 *   1. Default to **same-origin** ("") so authenticated browser requests do not trigger a
 *      CORS preflight OPTIONS. Dev uses Vite's proxy, prod uses ingress / nginx.
 *   2. Allow an explicit cross-origin escape hatch via VITE_FORCE_CROSS_ORIGIN_API=true
 *      AND VITE_API_BASE_URL (both required), but
 *   3. Hard-strip the absolute prefix when the configured URL happens to point back at the
 *      page's own origin — even a misconfigured deployment must stay same-origin. This is
 *      the safety net that prevents accidental OPTIONS calls.
 *   4. Warn loudly (once) when the SPA does end up using a cross-origin base, so
 *      regressions show up in the browser console instead of silently triggering preflights.
 */
let warnedAboutCrossOrigin = false

export function resolveSpaApiBaseOrigin(): string {
  const allowCrossOrigin = import.meta.env.VITE_FORCE_CROSS_ORIGIN_API === 'true'
  const raw = import.meta.env.VITE_API_BASE_URL?.trim()

  if (!allowCrossOrigin || !raw) return ''

  const trimmed = raw.replace(/\/$/, '')

  if (typeof window !== 'undefined' && window.location?.origin) {
    try {
      const configured = new URL(trimmed)
      if (configured.origin === window.location.origin) {
        // Configured URL points back at this page's origin — keep requests relative so the
        // browser does not classify them as cross-origin and skips the OPTIONS preflight.
        return ''
      }
    } catch {
      // Not a parseable absolute URL — treat as relative-prefix and keep it as is.
      return trimmed
    }
  }

  if (!warnedAboutCrossOrigin && typeof console !== 'undefined') {
    warnedAboutCrossOrigin = true
    console.warn(
      `[trader] SPA is configured to call ${trimmed} cross-origin (VITE_FORCE_CROSS_ORIGIN_API=true). ` +
        'Authenticated requests will trigger CORS preflight OPTIONS calls. ' +
        'Unset VITE_FORCE_CROSS_ORIGIN_API to keep same-origin /api and /hubs (no preflight).',
    )
  }

  return trimmed
}
