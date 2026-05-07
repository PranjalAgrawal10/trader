import type { ReactNode } from 'react'
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  useSyncExternalStore,
} from 'react'
import {
  applyEffectiveThemeToDocument,
  loadThemePreference,
  saveThemePreference,
  type ThemePreference,
} from './preference'

type ThemeContextValue = {
  preference: ThemePreference
  setPreference: (p: ThemePreference) => void
  effectiveTheme: 'light' | 'dark'
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

function subscribePrefersColorScheme(callback: () => void): () => void {
  const mq = window.matchMedia('(prefers-color-scheme: dark)')
  mq.addEventListener('change', callback)
  return () => mq.removeEventListener('change', callback)
}

function getSystemDarkSnapshot(): boolean {
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

function getServerSystemDarkSnapshot(): boolean {
  return false
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>(loadThemePreference)
  const systemDark = useSyncExternalStore(subscribePrefersColorScheme, getSystemDarkSnapshot, getServerSystemDarkSnapshot)

  const effectiveTheme = useMemo<'light' | 'dark'>(
    () => (preference === 'system' ? (systemDark ? 'dark' : 'light') : preference),
    [preference, systemDark],
  )

  useEffect(() => {
    applyEffectiveThemeToDocument(effectiveTheme)
  }, [effectiveTheme])

  const setPreference = useCallback((p: ThemePreference) => {
    setPreferenceState(p)
    saveThemePreference(p)
  }, [])

  const value = useMemo(
    () => ({ preference, setPreference, effectiveTheme }),
    [preference, setPreference, effectiveTheme],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (ctx == null) throw new Error('useTheme must be used within ThemeProvider')
  return ctx
}
