/**
 * Emits a self-contained review kit so a translator or agent works from one artifact instead of
 * cross-referencing the codebase.
 *
 *   node scripts/i18n-kit.mjs --area settings                 # authoring view (en + call sites)
 *   node scripts/i18n-kit.mjs --area settings --locale it-IT   # review view (adds the translation)
 *   node scripts/i18n-kit.mjs --all --locale ja-JP
 *   node scripts/i18n-kit.mjs --prefix shell.search,shell.capabilities --name search
 *
 * Output goes to scripts/kit-out/ and is generated, never committed.
 */
import { mkdirSync, writeFileSync, existsSync } from 'node:fs'
import { join } from 'node:path'
import {
  AREAS,
  CONTEXT_FILE,
  KIT_DIR,
  LOCALES_DIR,
  UI_SURFACE_GUIDANCE,
  areaOf,
  collectCallSites,
  flattenStrings,
  placeholderNames,
  readEnglishCatalog,
  readJson,
  sitesForKey,
} from './i18n-context-lib.mjs'

const args = process.argv.slice(2)
const valueOf = (name) => {
  const index = args.indexOf(`--${name}`)
  return index >= 0 ? args[index + 1] : undefined
}

const requestedArea = valueOf('area')
const locale = valueOf('locale')
const all = args.includes('--all')
const prefixes = valueOf('prefix')?.split(',').map(prefix => prefix.trim()).filter(Boolean)

if (!all && !requestedArea && !prefixes) {
  console.error(`Usage: node scripts/i18n-kit.mjs (--area <${Object.keys(AREAS).join('|')}> | --all | --prefix a.b,c.d [--name label]) [--locale <code>]`)
  process.exit(2)
}
if (requestedArea && !AREAS[requestedArea]) {
  console.error(`Unknown area "${requestedArea}". Known: ${Object.keys(AREAS).join(', ')}`)
  process.exit(2)
}

const english = readEnglishCatalog()
const notes = existsSync(CONTEXT_FILE) ? readJson(CONTEXT_FILE) : {}
const callSites = collectCallSites()

let target
if (locale) {
  const path = join(LOCALES_DIR, `${locale}.json`)
  if (!existsSync(path)) {
    console.error(`No catalog for locale ${locale}.`)
    process.exit(2)
  }
  target = flattenStrings(readJson(path).strings)
}

const areas = prefixes ? [valueOf('name') ?? 'selection'] : all ? Object.keys(AREAS) : [requestedArea]
mkdirSync(KIT_DIR, { recursive: true })

for (const area of areas) {
  const keys = [...english.keys()].filter(key =>
    prefixes
      ? prefixes.some(prefix => key === prefix || key.startsWith(`${prefix}.`))
      : areaOf(key) === area)
  if (keys.length === 0) continue

  const lines = [
    `# i18n kit — area "${area}"${locale ? ` — reviewing ${locale}` : ''}`,
    `# ${keys.length} keys, namespaces: ${(prefixes ?? AREAS[area]).join(', ')}`,
    '#',
    '# en  = source string (en-US, authoritative)',
    '# ui  = rendering surface; its register and length constraints are listed at the bottom',
    '# at  = call site in the source tree',
    '# ph  = interpolation placeholder',
    ...(locale ? [`# ${locale} = current translation`] : []),
    ...(Object.values(notes).some(note => note.context) ? ['# ctx = authored translator note'] : []),
    '',
  ]

  for (const key of keys) {
    const value = english.get(key)
    const note = notes[key] ?? {}
    lines.push(`## ${key}`)
    lines.push(`en    ${JSON.stringify(value)}`)
    if (locale) lines.push(`${locale} ${JSON.stringify(target.get(key) ?? '')}`)
    lines.push(`ui    ${note.ui ?? '?'}`)
    if (note.context) lines.push(`ctx   ${note.context}`)

    for (const name of placeholderNames(value)) {
      lines.push(`ph    {{${name}}}${note.placeholders?.[name] ? ` — ${note.placeholders[name]}` : ''}`)
    }
    if (note.joins) {
      for (const [field, text] of Object.entries(note.joins)) lines.push(`join  ${field}: ${text}`)
    }

    const sites = sitesForKey(key, callSites)
    const seen = new Set()
    for (const site of sites) {
      const where = `${site.file}:${site.line}`
      if (seen.has(where)) continue
      seen.add(where)
      lines.push(`at    ${where}${site.dynamic ? ' (dynamic key)' : ''}`)
    }
    if (sites.length === 0) lines.push('at    <no static call site — reached through a lookup table or a helper>')
    lines.push('')
  }

  lines.push('# ---- UI surface vocabulary ----')
  for (const [surface, guidance] of Object.entries(UI_SURFACE_GUIDANCE)) {
    lines.push(`# ${surface}: ${guidance}`)
  }

  const name = `${area}${locale ? `.${locale}` : ''}.txt`
  writeFileSync(join(KIT_DIR, name), `${lines.join('\n')}\n`, 'utf8')
  console.log(`${name}  (${keys.length} keys)`)
}
