import type { ResourceLanguage } from 'i18next'
import { matchSupportedLocale } from './resolution'

export interface LocaleMetadata {
  locale: string
  description: string
  nativeLabel: string
  englishLabel: string
}

interface LocaleFile {
  metadata: LocaleMetadata
  strings: ResourceLanguage
}

export interface LocaleCatalog extends LocaleMetadata {
  strings: ResourceLanguage
}

const localeFiles = import.meta.glob<LocaleFile>('../locales/*.json', {
  eager: true,
  import: 'default',
})

function requireText(value: unknown, field: string, path: string): string {
  if (typeof value !== 'string' || !value.trim()) {
    throw new Error(`Locale catalog ${path} must provide non-empty ${field}.`)
  }
  return value.trim()
}

function canonicalLocale(value: string, path: string): string {
  try {
    const [canonical] = Intl.getCanonicalLocales(value)
    if (!canonical || canonical !== value) {
      throw new Error()
    }
    return canonical
  } catch {
    throw new Error(`Locale catalog ${path} must use a canonical BCP 47 metadata.locale.`)
  }
}

export const localeCatalogs: readonly LocaleCatalog[] = Object.entries(localeFiles)
  .map(([path, file]) => {
    if (
      !file
      || typeof file !== 'object'
      || !file.metadata
      || !file.strings
      || typeof file.strings !== 'object'
      || Array.isArray(file.strings)
    ) {
      throw new Error(`Locale catalog ${path} must contain metadata and strings.`)
    }

    const locale = canonicalLocale(requireText(file.metadata.locale, 'metadata.locale', path), path)
    const filename = path.split(/[\\/]/).at(-1)?.replace(/\.json$/i, '')
    if (filename !== locale) {
      throw new Error(`Locale catalog ${path} filename must match metadata.locale ${locale}.`)
    }

    return {
      locale,
      description: requireText(file.metadata.description, 'metadata.description', path),
      nativeLabel: requireText(file.metadata.nativeLabel, 'metadata.nativeLabel', path),
      englishLabel: requireText(file.metadata.englishLabel, 'metadata.englishLabel', path),
      strings: file.strings,
    }
  })
  .sort((a, b) => a.nativeLabel.localeCompare(b.nativeLabel))

for (const field of ['locale', 'description', 'nativeLabel', 'englishLabel'] as const) {
  const duplicate = localeCatalogs.find((catalog, index) =>
    localeCatalogs.some((other, otherIndex) =>
      otherIndex !== index && other[field].toLowerCase() === catalog[field].toLowerCase()))
  if (duplicate) {
    throw new Error(`Locale catalog metadata.${field} must be unique; duplicate ${duplicate[field]}.`)
  }
}

function flattenStrings(
  value: unknown,
  prefix = '',
  out: Record<string, string> = {},
): Record<string, string> {
  if (typeof value === 'string') {
    if (!value.trim()) throw new Error(`Locale key ${prefix} must not be empty.`)
    out[prefix] = value
    return out
  }

  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`Locale key ${prefix || '<root>'} must contain a string or nested object.`)
  }

  for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
    flattenStrings(child, prefix ? `${prefix}.${key}` : key, out)
  }
  return out
}

function placeholders(value: string): string[] {
  return [...value.matchAll(/\{\{\s*([^,}\s]+)(?:,[^}]*)?\s*\}\}/g)]
    .map(match => match[1])
    .sort()
}

function componentTags(value: string): string[] {
  return [...value.matchAll(/<\/?[A-Za-z][A-Za-z0-9_-]*\b[^>]*>/g)]
    .map(match => match[0])
    .sort()
}

const fallbackCatalog = localeCatalogs.find(catalog => catalog.locale === 'en-US')
if (!fallbackCatalog) throw new Error('A canonical en-US locale catalog is required.')

const fallbackStrings = flattenStrings(fallbackCatalog.strings)
const fallbackKeys = Object.keys(fallbackStrings).sort()

for (const catalog of localeCatalogs) {
  const strings = flattenStrings(catalog.strings)
  const keys = Object.keys(strings).sort()
  const missing = fallbackKeys.filter(key => !(key in strings))
  const extra = keys.filter(key => !(key in fallbackStrings))
  if (missing.length || extra.length) {
    throw new Error(
      `Locale ${catalog.locale} must match en-US keys. Missing: ${missing.join(', ') || 'none'}; extra: ${extra.join(', ') || 'none'}.`,
    )
  }

  for (const key of fallbackKeys) {
    const expected = placeholders(fallbackStrings[key])
    const actual = placeholders(strings[key])
    if (expected.join('\0') !== actual.join('\0')) {
      throw new Error(`Locale ${catalog.locale} key ${key} must preserve interpolation placeholders.`)
    }

    const expectedTags = componentTags(fallbackStrings[key])
    const actualTags = componentTags(strings[key])
    if (expectedTags.join('\0') !== actualTags.join('\0')) {
      throw new Error(`Locale ${catalog.locale} key ${key} must preserve named component tags.`)
    }
  }
}

export const supportedLocales: readonly string[] = localeCatalogs.map(catalog => catalog.locale)

export function getSupportedLocale(locale: unknown): string | undefined {
  return matchSupportedLocale(locale, supportedLocales)
}

export function getLocaleCatalog(locale: unknown): LocaleCatalog | undefined {
  const supported = getSupportedLocale(locale)
  return supported ? localeCatalogs.find(catalog => catalog.locale === supported) : undefined
}
