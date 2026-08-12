import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { localeCatalogs } from './catalogs'

interface ContextNote {
  en: string
  ui: string
  context: string
  placeholders?: Record<string, string>
  joins?: Record<string, string>
}

// Read rather than import: the sidecar is large, and typing it as an object literal would cost more
// in `tsc -b` than the test gains from it.
function readSidecar<T>(name: string): T {
  const path = fileURLToPath(new URL(`../locales/_context/${name}`, import.meta.url))
  return JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, '')) as T
}

const notes = readSidecar<Record<string, ContextNote>>('en-US.json')
const surfaces = new Set(Object.keys(readSidecar<Record<string, string>>('ui-surfaces.json')))

function flattenStrings(
  value: unknown,
  prefix = '',
  out: Record<string, string> = {},
): Record<string, string> {
  if (typeof value === 'string') {
    out[prefix] = value
    return out
  }
  for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
    flattenStrings(child, prefix ? `${prefix}.${key}` : key, out)
  }
  return out
}

function placeholderNames(value: string): string[] {
  const names = [...value.matchAll(/\{\{\s*([^,}\s]+)(?:,[^}]*)?\s*\}\}/g)].map(match => match[1])
  return [...new Set(names)]
}

const english = flattenStrings(
  localeCatalogs.find(catalog => catalog.locale === 'en-US')!.strings,
)
const englishKeys = Object.keys(english)

describe('translator context sidecar', () => {
  it('documents every en-US key and nothing else', () => {
    const undocumented = englishKeys.filter(key => !notes[key])
    const orphaned = Object.keys(notes).filter(key => !(key in english))

    expect(undocumented, 'keys missing a note — run npm run i18n:scaffold').toEqual([])
    expect(orphaned, 'notes for keys that no longer exist — run npm run i18n:scaffold').toEqual([])
  })

  it('carries a non-empty note for every key', () => {
    const empty = englishKeys.filter(key => !notes[key]?.context?.trim())
    expect(empty).toEqual([])
  })

  it('uses only UI surfaces from the controlled vocabulary', () => {
    const invalid = englishKeys
      .filter(key => !surfaces.has(notes[key]?.ui))
      .map(key => `${key} (${notes[key]?.ui})`)
    expect(invalid).toEqual([])
  })

  // The staleness guard. A note written against wording that has since changed is worse than no
  // note, so changing an English string forces its note to be revisited in the same commit.
  it('records the English source each note was written against', () => {
    const stale = englishKeys
      .filter(key => notes[key]?.en !== english[key])
      .map(key => `${key}: note says "${notes[key]?.en}", catalog says "${english[key]}"`)
    expect(stale, 'revisit these notes, then re-apply them to refresh en').toEqual([])
  })

  it('documents every interpolation placeholder', () => {
    const gaps: string[] = []
    for (const key of englishKeys) {
      const required = placeholderNames(english[key])
      const documented = notes[key]?.placeholders ?? {}
      for (const name of required) {
        if (!documented[name]?.trim()) gaps.push(`${key} → {{${name}}}`)
      }
      for (const name of Object.keys(documented)) {
        if (!required.includes(name)) gaps.push(`${key} → {{${name}}} is not in the string`)
      }
    }
    expect(gaps).toEqual([])
  })

  it('explains how every fragment is joined to its surroundings', () => {
    const unexplained = englishKeys.filter(key =>
      notes[key]?.ui === 'fragment' && !notes[key]?.joins?.example?.trim())
    expect(unexplained, 'fragments need joins.example showing the assembled result').toEqual([])
  })

  it('is not mistaken for a shippable locale catalog', () => {
    expect(localeCatalogs.map(catalog => catalog.locale)).not.toContain('_context')
    expect(localeCatalogs).toHaveLength(6)
  })
})
