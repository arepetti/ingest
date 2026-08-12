export function matchSupportedLocale(
  locale: unknown,
  supportedLocales: readonly string[],
): string | undefined {
  if (typeof locale !== 'string') return undefined
  const candidate = locale.trim()
  if (!candidate) return undefined
  return supportedLocales.find(supported => supported.toLowerCase() === candidate.toLowerCase())
}

export function resolveLocalePreference(
  storedLocale: unknown,
  configuredDefaultLocale: unknown,
  supportedLocales: readonly string[],
  fallbackLocale: string,
): string {
  return matchSupportedLocale(storedLocale, supportedLocales)
    ?? matchSupportedLocale(configuredDefaultLocale, supportedLocales)
    ?? matchSupportedLocale(fallbackLocale, supportedLocales)
    ?? fallbackLocale
}
