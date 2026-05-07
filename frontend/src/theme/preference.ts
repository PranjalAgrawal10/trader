/** localStorage — profile Appearance + boot-time paint (see <code>main.tsx</code>). */
export const THEME_PREFERENCE_STORAGE_KEY = 'trader-theme-preference'

export type ThemePreference = 'light' | 'dark' | 'system'

export function loadThemePreference(): ThemePreference {
  try {
    const v = localStorage.getItem(THEME_PREFERENCE_STORAGE_KEY)
    if (v === 'light' || v === 'dark' || v === 'system') return v
  } catch {
    /* private mode / unavailable */
  }
  return 'dark'
}

export function saveThemePreference(p: ThemePreference): void {
  try {
    localStorage.setItem(THEME_PREFERENCE_STORAGE_KEY, p)
  } catch {
    /* ignore */
  }
}

export function readSystemPrefersDark(): boolean {
  return window.matchMedia?.('(prefers-color-scheme: dark)')?.matches ?? false
}

export function resolveEffectiveTheme(preference: ThemePreference): 'light' | 'dark' {
  if (preference === 'system') return readSystemPrefersDark() ? 'dark' : 'light'
  return preference
}

export function applyEffectiveThemeToDocument(effective: 'light' | 'dark'): void {
  document.documentElement.setAttribute('data-bs-theme', effective)
}

/** Run before React mounts so the first paint matches the saved preference. */
export function initializeDocumentThemeFromStorage(): void {
  applyEffectiveThemeToDocument(resolveEffectiveTheme(loadThemePreference()))
}
