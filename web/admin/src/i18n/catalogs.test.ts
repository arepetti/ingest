import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { localeCatalogs, supportedLocales } from './catalogs'

const sourceFiles = import.meta.glob<string>('../**/*.{ts,tsx}', {
  eager: true,
  query: '?raw',
  import: 'default',
})

const diagnosticSource = readFileSync(
  fileURLToPath(new URL('../../../../src/Ingest.Core/Common/Diagnostic.cs', import.meta.url)),
  'utf8',
)

function flattenKeys(value: unknown, prefix = '', keys: Set<string> = new Set()): Set<string> {
  if (typeof value === 'string') {
    keys.add(prefix)
    return keys
  }

  for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
    flattenKeys(child, prefix ? `${prefix}.${key}` : key, keys)
  }
  return keys
}

function staticTranslationKeys(source: string): string[] {
  const keys: string[] = []
  const callPattern = /\b(?:t|i18n\.t)\(\s*(['"`])([^'"`]+)\1/g
  const transPattern = /\bi18nKey\s*=\s*(['"])([^'"]+)\1/g

  for (const pattern of [callPattern, transPattern]) {
    for (const match of source.matchAll(pattern)) {
      if (!match[2].includes('${')) keys.push(match[2])
    }
  }
  return keys
}

describe('locale catalogs', () => {
  it('discovers every shipped locale catalog', () => {
    expect([...supportedLocales].sort()).toEqual([
      'en-GB',
      'en-US',
      'it-IT',
      'ja-JP',
      'zh-CN',
      'zh-TW',
    ])
    expect(localeCatalogs).toHaveLength(6)
  })

  it('contains every statically referenced translation key', () => {
    const english = localeCatalogs.find(catalog => catalog.locale === 'en-US')
    expect(english).toBeDefined()
    const catalogKeys = flattenKeys(english!.strings)
    const references = Object.entries(sourceFiles)
      .flatMap(([path, source]) =>
        staticTranslationKeys(source).map(key => ({ key, path })))
    const missing = references.filter(reference =>
      !catalogKeys.has(reference.key)
      && ![...catalogKeys].some(key => key.startsWith(`${reference.key}_`)))

    expect(missing).toEqual([])
  })

  it('covers every backend diagnostic code in every locale', () => {
    const backendCodes = [...diagnosticSource.matchAll(
      /public const string \w+\s*=\s*"([^"]+)";/g,
    )].map(match => match[1])

    expect(backendCodes.length).toBeGreaterThan(0)
    expect(new Set(backendCodes).size).toBe(backendCodes.length)
    for (const catalog of localeCatalogs) {
      const catalogKeys = flattenKeys(catalog.strings)
      const missing = backendCodes.filter(code => !catalogKeys.has(`apiMessages.${code}`))
      expect(missing, catalog.locale).toEqual([])
    }
  })
})
