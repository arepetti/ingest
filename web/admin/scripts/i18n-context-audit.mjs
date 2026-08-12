/**
 * Reports quality problems in the context sidecar that `context.test.ts` cannot express as a pass or
 * a fail: notes that merely restate the English, boilerplate pasted across many keys, prose that
 * leaks implementation detail at the translator, and placeholder descriptions with no example.
 *
 *   node scripts/i18n-context-audit.mjs [--area shell] [--verbose]
 *
 * Advisory, not a gate. It exits non-zero only when a note is unusably thin.
 */
import { CONTEXT_FILE, areaOf, readEnglishCatalog, readJson } from './i18n-context-lib.mjs'

const args = process.argv.slice(2)
const areaFilter = args.includes('--area') ? args[args.indexOf('--area') + 1] : undefined
const verbose = args.includes('--verbose')

const english = readEnglishCatalog()
const notes = readJson(CONTEXT_FILE)
const selected = [...english.keys()].filter(key => !areaFilter || areaOf(key) === areaFilter)
// Unauthored skeletons carry a guessed surface and no prose; counting them would flatter the report.
const keys = selected.filter(key => notes[key]?.context?.trim())

// Phrases that mean the note is addressing a developer rather than a translator.
const LEAKS = [
  /\bthis string\b/i,
  /\bsidecar\b/i,
  /\.tsx?\b/,
  /\ben-US\.json\b/,
  /\bcall site\b/i,
  /\bthe code\b/i,
  /\bi18next\b/i,
  /\bTODO\b/,
]

const thin = []
const leaky = []
const restating = []
const exampleless = []
const contexts = new Map()

for (const key of keys) {
  const note = notes[key]
  if (!note?.context) continue
  const context = note.context.trim()
  const value = english.get(key)

  if (context.length < 40) thin.push(`${key}: ${context}`)

  const leak = LEAKS.find(pattern => pattern.test(context))
  if (leak) leaky.push(`${key}: ${context}`)

  // A note that quotes the English and adds almost nothing is not a note.
  const stripped = context.toLowerCase().replace(/[^a-z0-9]+/g, ' ')
  const englishStripped = value.toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim()
  if (englishStripped.length > 12 && stripped.includes(englishStripped) && context.length < value.length * 2.5) {
    restating.push(`${key}: "${value}" → ${context}`)
  }

  if (!contexts.has(context)) contexts.set(context, [])
  contexts.get(context).push(key)

  for (const [name, description] of Object.entries(note.placeholders ?? {})) {
    if (!/e\.g\.|for example|such as|\d/.test(description)) {
      exampleless.push(`${key} → {{${name}}}: ${description}`)
    }
  }
}

const duplicated = [...contexts.entries()]
  .filter(([, group]) => group.length > 2)
  .sort((a, b) => b[1].length - a[1].length)

const report = (title, items, cap = 15) => {
  console.log(`\n${title}: ${items.length}`)
  for (const item of items.slice(0, verbose ? items.length : cap)) console.log(`  ${item}`)
  if (!verbose && items.length > cap) console.log(`  … and ${items.length - cap} more (--verbose)`)
}

console.log(`Audited ${keys.length} authored note(s) of ${selected.length}${areaFilter ? ` in area "${areaFilter}"` : ''}.`)
report('Thin — under 40 characters', thin)
report('Leaking implementation detail at the translator', leaky)
report('Restating the English without adding meaning', restating)
report('Placeholder descriptions with no concrete example', exampleless)

console.log(`\nIdentical prose reused across 3+ keys: ${duplicated.length} block(s)`)
for (const [context, group] of duplicated.slice(0, verbose ? duplicated.length : 10)) {
  console.log(`  ${group.length}× ${group[0]}${group.length > 1 ? ` (+${group.length - 1})` : ''}`)
  console.log(`      ${context.slice(0, 140)}${context.length > 140 ? '…' : ''}`)
}

const surfaces = new Map()
for (const key of keys) surfaces.set(notes[key]?.ui, (surfaces.get(notes[key]?.ui) ?? 0) + 1)
console.log('\nSurfaces:')
for (const [surface, count] of [...surfaces].sort((a, b) => b[1] - a[1])) {
  console.log(`  ${String(count).padStart(5)}  ${surface}`)
}

if (thin.length > 0) {
  console.log('\nThin notes must be rewritten before this is useful to a translator.')
  process.exit(1)
}
