/**
 * Folds authored notes into the context sidecar.
 *
 *   node scripts/i18n-context-apply.mjs scripts/context-parts/settings.json [--dry-run]
 *
 * A part file is a flat dotted map of the keys it documents:
 *
 *   { "settings.common.saving": { "ui": "buttonProgress", "context": "…",
 *     "placeholders": { "name": "…" }, "joins": { "after": "…", "example": "…" } } }
 *
 * `context` is required for a key that has no note yet; for one that already has authored prose you
 * may send `ui` alone to correct the surface without restating the note. Applying a note refreshes
 * its `en` from the live catalog, which is how a note flagged stale by `context.test.ts` gets
 * cleared: revisit the prose, re-apply.
 */
import { writeFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  ADMIN_ROOT,
  CONTEXT_FILE,
  UI_SURFACES,
  placeholderNames,
  readEnglishCatalog,
  readJson,
} from './i18n-context-lib.mjs'

const args = process.argv.slice(2)
const dryRun = args.includes('--dry-run')
const partPaths = args.filter(argument => !argument.startsWith('--'))

if (partPaths.length === 0) {
  console.error('Usage: node scripts/i18n-context-apply.mjs <part.json...> [--dry-run]')
  process.exit(2)
}
if (!existsSync(CONTEXT_FILE)) {
  console.error('No context sidecar yet. Run: npm run i18n:scaffold')
  process.exit(2)
}

const english = readEnglishCatalog()
const notes = readJson(CONTEXT_FILE)
const problems = []
let applied = 0

for (const partPath of partPaths) {
  const full = resolve(ADMIN_ROOT, partPath)
  const part = readJson(full)

  for (let [key, incoming] of Object.entries(part)) {
    const where = `${partPath} → ${key}`
    if (!english.has(key)) {
      problems.push(`${where}: not an en-US key`)
      continue
    }
    const existing = notes[key]?.context?.trim()
    if (incoming.context === undefined && existing) {
      // A surface-only correction: keep the authored prose rather than making the caller repeat it.
      incoming = { ...incoming, context: existing }
    }
    if (typeof incoming.context !== 'string' || !incoming.context.trim()) {
      problems.push(`${where}: context must be non-empty`)
      continue
    }
    if (incoming.ui !== undefined && !UI_SURFACES.includes(incoming.ui)) {
      problems.push(`${where}: ui "${incoming.ui}" is not in the vocabulary`)
      continue
    }

    const value = english.get(key)
    const required = placeholderNames(value)
    const documented = incoming.placeholders ?? notes[key]?.placeholders ?? {}
    const missing = required.filter(name => !documented[name]?.trim())
    if (missing.length > 0) {
      problems.push(`${where}: undocumented placeholder(s) ${missing.join(', ')}`)
      continue
    }
    const unknown = Object.keys(incoming.placeholders ?? {}).filter(name => !required.includes(name))
    if (unknown.length > 0) {
      problems.push(`${where}: placeholder(s) ${unknown.join(', ')} are not in the string`)
      continue
    }

    const note = { en: value, ui: incoming.ui ?? notes[key]?.ui, context: incoming.context.trim() }
    if (required.length > 0) {
      note.placeholders = {}
      for (const name of required) note.placeholders[name] = documented[name].trim()
    }
    const joins = incoming.joins ?? notes[key]?.joins
    if (joins) note.joins = joins

    notes[key] = note
    applied += 1
  }
}

if (problems.length > 0) {
  console.error(`${problems.length} problem(s):`)
  for (const problem of problems) console.error(`  ${problem}`)
  process.exit(1)
}

// Re-emit in catalog order so the sidecar always reads alongside en-US.json.
const ordered = {}
for (const key of english.keys()) if (notes[key]) ordered[key] = notes[key]

if (!dryRun) writeFileSync(CONTEXT_FILE, `${JSON.stringify(ordered, null, 2)}\n`, 'utf8')

const authored = Object.values(ordered).filter(note => note.context?.trim()).length
console.log(`${dryRun ? 'Validated' : 'Applied'} ${applied} note(s).`)
console.log(`Coverage: ${authored}/${english.size} (${Math.round((authored / english.size) * 100)}%)`)
