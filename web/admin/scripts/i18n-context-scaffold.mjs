/**
 * Creates or refreshes `src/locales/_context/en-US.json`, the translator-context sidecar.
 *
 *   node scripts/i18n-context-scaffold.mjs [--check]
 *
 * Adds a skeleton note for every en-US key, drops notes whose key no longer exists, and fills in
 * the mechanical parts (`en`, placeholder names, a `ui` guess derived from call sites). Authored
 * `context` prose is never touched.
 *
 * When the English text of an already-authored note changes, the note is deliberately left with its
 * old `en` so that `context.test.ts` fails: whoever changed the wording has to revisit the note.
 * Re-authoring the key through `i18n-context-apply.mjs` clears the staleness.
 *
 * `--check` exits non-zero if the file is not what a run would produce, for use in CI.
 */
import { mkdirSync, writeFileSync, existsSync, readFileSync } from 'node:fs'
import {
  CONTEXT_DIR,
  CONTEXT_FILE,
  UI_SURFACES,
  collectCallSites,
  guessSurface,
  placeholderNames,
  readEnglishCatalog,
  readJson,
  sitesForKey,
} from './i18n-context-lib.mjs'

const check = process.argv.includes('--check')

const english = readEnglishCatalog()
const callSites = collectCallSites()
const existing = existsSync(CONTEXT_FILE) ? readJson(CONTEXT_FILE) : {}

const PLURAL_SUFFIX = /_(?:zero|one|two|few|many|other)$/

const notes = {}
const stale = []
const located = []

for (const [key, value] of english) {
  const previous = existing[key] ?? {}
  const authored = typeof previous.context === 'string' && previous.context.trim().length > 0
  const sites = sitesForKey(key, callSites)
  if (sites.length > 0) located.push(key)

  if (authored && previous.en !== value) stale.push(key)

  const note = {
    // Keep the stale English on authored notes so the guard trips; refresh it while unauthored.
    en: authored ? previous.en ?? value : value,
    // An author's surface choice sticks; an unauthored guess is re-derived as the call sites move.
    ui: authored && UI_SURFACES.includes(previous.ui) ? previous.ui : guessSurface(key, sites, value),
    context: authored ? previous.context : '',
  }

  const names = placeholderNames(value)
  if (names.length > 0) {
    note.placeholders = {}
    for (const name of names) {
      note.placeholders[name] = previous.placeholders?.[name] ?? ''
    }
  }
  if (previous.joins) note.joins = previous.joins

  notes[key] = note
}

// Plural siblings describe one message, so one authored note covers the whole group.
const groups = new Map()
for (const key of Object.keys(notes)) {
  if (!PLURAL_SUFFIX.test(key)) continue
  const base = key.replace(PLURAL_SUFFIX, '')
  if (!groups.has(base)) groups.set(base, [])
  groups.get(base).push(key)
}
for (const siblings of groups.values()) {
  const source = siblings.find(key => notes[key].context.trim().length > 0)
  if (!source) continue
  for (const key of siblings) {
    if (notes[key].context.trim().length > 0) continue
    notes[key].context = notes[source].context
    notes[key].ui = notes[source].ui
    if (notes[source].joins && !notes[key].joins) notes[key].joins = notes[source].joins
    for (const [name, description] of Object.entries(notes[key].placeholders ?? {})) {
      if (!description) notes[key].placeholders[name] = notes[source].placeholders?.[name] ?? ''
    }
  }
}

const orphans = Object.keys(existing).filter(key => !english.has(key))
const serialised = `${JSON.stringify(notes, null, 2)}\n`

if (check) {
  const current = existsSync(CONTEXT_FILE) ? readFileSync(CONTEXT_FILE, 'utf8').replace(/^\uFEFF/, '') : ''
  if (current !== serialised) {
    console.error('Context sidecar is out of date. Run: npm run i18n:scaffold')
    process.exit(1)
  }
  console.log(`Context sidecar is up to date (${english.size} keys).`)
  process.exit(0)
}

mkdirSync(CONTEXT_DIR, { recursive: true })
writeFileSync(CONTEXT_FILE, serialised, 'utf8')

const authoredCount = Object.values(notes).filter(note => note.context.trim().length > 0).length
const undocumented = Object.entries(notes)
  .filter(([, note]) => Object.values(note.placeholders ?? {}).some(description => !description))
  .length

console.log(`Keys:        ${english.size}`)
console.log(`Authored:    ${authoredCount} (${Math.round((authoredCount / english.size) * 100)}%)`)
console.log(`Call sites:  ${located.length} keys statically located`)
console.log(`Placeholders pending: ${undocumented} keys`)
if (orphans.length > 0) console.log(`Removed ${orphans.length} orphaned note(s): ${orphans.slice(0, 10).join(', ')}`)
if (stale.length > 0) {
  console.log(`\nSTALE — English changed since the note was written (${stale.length}):`)
  for (const key of stale) console.log(`  ${key}`)
}
