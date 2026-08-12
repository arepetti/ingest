import i18n, { FALLBACK_LOCALE } from '../i18n'

/** The active UI locale, suitable for the browser's Intl APIs. */
export function currentLocale(): string {
  return i18n.resolvedLanguage || i18n.language || FALLBACK_LOCALE
}

/** Format an ISO timestamp as a locale date (no time). Returns an em-dash for empty/invalid input. */
export function formatDate(
  iso?: string | null,
  options?: Intl.DateTimeFormatOptions,
): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString(currentLocale(), options)
}

/** Format an ISO timestamp as a full locale date + time. Returns an em-dash for empty/invalid input. */
export function formatDateTime(
  iso?: string | null,
  options?: Intl.DateTimeFormatOptions,
): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString(currentLocale(), options)
}

/** Format a number using the active UI locale. */
export function formatNumber(
  value: number,
  options?: Intl.NumberFormatOptions,
): string {
  return new Intl.NumberFormat(currentLocale(), options).format(value)
}

/** Create a reusable date formatter bound to the active UI locale. */
export function dateTimeFormatter(options?: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  return new Intl.DateTimeFormat(currentLocale(), options)
}
