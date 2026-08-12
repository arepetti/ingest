import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import {
  getSupportedLocale,
  localeCatalogs,
  supportedLocales,
} from './catalogs'
import { resolveLocalePreference } from './resolution'

export { getLocaleCatalog, getSupportedLocale, localeCatalogs, supportedLocales } from './catalogs'
export { matchSupportedLocale, resolveLocalePreference } from './resolution'
export type { LocaleCatalog, LocaleMetadata } from './catalogs'

export const LOCALE_STORAGE_KEY = 'ingest.locale'
export const FALLBACK_LOCALE = 'en-US'

if (!getSupportedLocale(FALLBACK_LOCALE)) {
  throw new Error(`The fallback locale ${FALLBACK_LOCALE} needs a locale catalog.`)
}

const resources = Object.fromEntries(
  localeCatalogs.map(catalog => [catalog.locale, { translation: catalog.strings }]),
)

export function resolveLocale(storedLocale: unknown, configuredDefaultLocale: unknown): string {
  return resolveLocalePreference(
    storedLocale,
    configuredDefaultLocale,
    supportedLocales,
    FALLBACK_LOCALE,
  )
}

function readStoredLocale(): string | null {
  try {
    return localStorage.getItem(LOCALE_STORAGE_KEY)
  } catch {
    return null
  }
}

function persistLocale(locale: string): void {
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, locale)
  } catch {
    // Applying the locale still works when storage is unavailable.
  }
}

export async function loadBootstrapDefaultLocale(): Promise<unknown> {
  try {
    const response = await fetch('/api/bootstrap', { credentials: 'include' })
    if (!response.ok) return undefined

    const body: unknown = await response.json()
    if (!body || typeof body !== 'object') return undefined
    return (body as { defaultLocale?: unknown }).defaultLocale
  } catch {
    return undefined
  }
}

function updateDocumentLocale(locale: string): void {
  if (typeof document === 'undefined') return
  document.documentElement.lang = getSupportedLocale(locale) ?? FALLBACK_LOCALE
  document.title = i18n.t('app.title')
}

i18n.on('languageChanged', updateDocumentLocale)

export async function initializeI18n(): Promise<string> {
  const configuredDefaultLocale = await loadBootstrapDefaultLocale()
  const effectiveLocale = resolveLocale(readStoredLocale(), configuredDefaultLocale)

  if (!i18n.isInitialized) {
    await i18n
      .use(initReactI18next)
      .init({
        resources,
        lng: effectiveLocale,
        fallbackLng: FALLBACK_LOCALE,
        supportedLngs: [...supportedLocales],
        load: 'currentOnly',
        interpolation: { escapeValue: false },
      })
  } else {
    await i18n.changeLanguage(effectiveLocale)
  }

  updateDocumentLocale(effectiveLocale)
  return effectiveLocale
}

export async function setLocale(locale: unknown): Promise<boolean> {
  const supportedLocale = getSupportedLocale(locale)
  if (!supportedLocale) return false

  persistLocale(supportedLocale)
  await i18n.changeLanguage(supportedLocale)
  return true
}

export default i18n
