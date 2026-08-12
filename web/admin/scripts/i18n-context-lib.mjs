/**
 * Shared helpers for the translator-context tooling: reading the en-US catalog, locating `t()` call
 * sites in the source tree, and the controlled vocabulary of UI surfaces.
 *
 * The context file itself lives outside the `../locales/*.json` glob that `src/i18n/catalogs.ts`
 * uses, so it can never be mistaken for a shippable locale catalog.
 */
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, relative, resolve } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))

export const ADMIN_ROOT = resolve(here, '..')
export const SRC_DIR = join(ADMIN_ROOT, 'src')
export const LOCALES_DIR = join(SRC_DIR, 'locales')
export const CONTEXT_DIR = join(LOCALES_DIR, '_context')
export const CONTEXT_FILE = join(CONTEXT_DIR, 'en-US.json')
export const PARTS_DIR = join(here, 'context-parts')
export const KIT_DIR = join(here, 'kit-out')

export const SURFACES_FILE = join(CONTEXT_DIR, 'ui-surfaces.json')

export function readJson(path) {
  return JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''))
}

/**
 * UI surface each string is rendered on, with the register and length constraints it implies. A
 * closed set on purpose: it turns "what tone does this need?" into a lookup instead of a judgement
 * call, and `context.test.ts` enforces membership.
 */
export const UI_SURFACE_GUIDANCE = readJson(SURFACES_FILE)
export const UI_SURFACES = Object.keys(UI_SURFACE_GUIDANCE)

/** Flatten the catalog's `strings` tree to dotted keys, preserving document order. */
export function flattenStrings(value, prefix = '', out = new Map()) {
  if (typeof value === 'string') {
    out.set(prefix, value)
    return out
  }
  for (const [key, child] of Object.entries(value)) {
    flattenStrings(child, prefix ? `${prefix}.${key}` : key, out)
  }
  return out
}

export function readEnglishCatalog() {
  return flattenStrings(readJson(join(LOCALES_DIR, 'en-US.json')).strings)
}

/** i18next interpolation names, in first-seen order. */
export function placeholderNames(value) {
  const seen = []
  for (const match of value.matchAll(/\{\{\s*([^,}\s]+)(?:,[^}]*)?\s*\}\}/g)) {
    if (!seen.includes(match[1])) seen.push(match[1])
  }
  return seen
}

/** Named component tags used by `<Trans>` interpolation, e.g. `<savedAt />`. */
export function componentTags(value) {
  return [...value.matchAll(/<\/?[A-Za-z][A-Za-z0-9_-]*\b[^>]*>/g)].map(m => m[0])
}

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry)
    if (statSync(full).isDirectory()) {
      if (entry !== 'locales' && entry !== 'node_modules') walk(full, out)
    } else if (/\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry)) {
      out.push(full)
    }
  }
  return out
}

// Attribute prefix -> UI surface. Checked against the text immediately before the `t(` call.
const PROP_SURFACES = [
  [/aria-label\s*=\s*\{\s*$/, 'ariaLabel'],
  [/aria-describedby\s*=\s*\{\s*$/, 'ariaLabel'],
  [/placeholder\s*=\s*\{\s*$/, 'placeholder'],
  [/hint\s*=\s*\{\s*$/, 'fieldHint'],
  [/validationMessage\s*=\s*\{\s*$/, 'validationError'],
  [/label\s*=\s*\{\s*$/, 'fieldLabel'],
  [/title\s*=\s*\{\s*$/, 'tooltip'],
  [/content\s*=\s*\{\s*$/, 'tooltip'],
  [/(?:text|value)\s*=\s*\{\s*$/, 'button'],
  [/(?:header|heading)\s*=\s*\{\s*$/, 'sectionTitle'],
]

// Enclosing JSX element -> UI surface, for calls in children position.
const ELEMENT_SURFACES = {
  Button: 'button',
  ToolbarButton: 'button',
  CompoundButton: 'button',
  MenuItem: 'menuItem',
  MenuItemRadio: 'menuItem',
  MenuItemCheckbox: 'menuItem',
  Option: 'menuItem',
  Link: 'button',
  Tab: 'navItem',
  TableHeaderCell: 'columnHeader',
  DataGridHeaderCell: 'columnHeader',
  Badge: 'statusBadge',
  CounterBadge: 'statusBadge',
  MessageBarBody: 'toast',
  MessageBarTitle: 'toast',
  DialogTitle: 'dialogTitle',
  DialogContent: 'dialogBody',
  Title1: 'pageTitle',
  Title2: 'pageTitle',
  Title3: 'sectionTitle',
  Subtitle1: 'sectionTitle',
  Subtitle2: 'sectionTitle',
  Label: 'fieldLabel',
}

/** Callers that make the surface obvious regardless of JSX position. */
const CALLER_SURFACES = [
  [/confirm\w*\(\s*$/i, 'dialogBody'],
  [/set(?:ActionInfo|Info|Status|Toast|Message)\s*\(\s*$/, 'toast'],
  [/set(?:SubmitError|Error|ActionError)\s*\(\s*$/, 'validationError'],
]

/**
 * Scan the source tree for translation-key references, returning a map of key (or dynamic key
 * prefix ending in `.`) to the call sites that use it.
 */
export function collectCallSites() {
  const sites = new Map()
  const add = (key, site) => {
    if (!sites.has(key)) sites.set(key, [])
    sites.get(key).push(site)
  }

  for (const file of walk(SRC_DIR)) {
    const source = readFileSync(file, 'utf8')
    const rel = relative(ADMIN_ROOT, file).replace(/\\/g, '/')
    const lineStarts = [...source.matchAll(/\n/g)].map(m => m.index)
    const lineOf = (index) => lineStarts.filter(start => start < index).length + 1

    const patterns = [
      /\b(?:t|i18n\.t)\(\s*(['"`])([^'"`]*)\1/g,
      /\bi18nKey\s*=\s*(['"])([^'"]*)\1/g,
    ]

    for (const pattern of patterns) {
      for (const match of source.matchAll(pattern)) {
        const raw = match[2]
        const before = source.slice(Math.max(0, match.index - 120), match.index)
        const dynamic = /\$\{/.test(raw)
        // `t(`shell.valueType.${type}`)` documents every key under `shell.valueType.`
        const key = dynamic ? raw.slice(0, raw.indexOf('${')) : raw
        if (!key) continue

        let surface
        for (const [re, value] of CALLER_SURFACES) if (re.test(before)) { surface = value; break }
        if (!surface) for (const [re, value] of PROP_SURFACES) if (re.test(before)) { surface = value; break }
        if (!surface) {
          const tags = [...before.matchAll(/<([A-Z][A-Za-z0-9]*)\b/g)].map(m => m[1])
          const enclosing = tags.at(-1)
          if (enclosing && ELEMENT_SURFACES[enclosing]) surface = ELEMENT_SURFACES[enclosing]
        }

        add(key, { file: rel, line: lineOf(match.index), surface, dynamic })
      }
    }
  }
  return sites
}

/**
 * Resolve the call sites that apply to a catalog key, following i18next's plural suffixes and the
 * dynamic-prefix references recorded by {@link collectCallSites}.
 */
export function sitesForKey(key, sites) {
  const found = []
  const direct = sites.get(key)
  if (direct) found.push(...direct)

  // `t('a.b')` with a `count` option resolves to `a.b_one` / `a.b_other`.
  const pluralBase = key.replace(/_(?:zero|one|two|few|many|other)$/, '')
  if (pluralBase !== key && sites.has(pluralBase)) found.push(...sites.get(pluralBase))

  for (const [candidate, candidateSites] of sites) {
    if (candidate.endsWith('.') && key.startsWith(candidate)) found.push(...candidateSites)
  }
  return found
}

/** Best-guess UI surface for a key, from its value, its namespace and its call sites. */
export function guessSurface(key, siteList, value = '') {
  if (key.startsWith('apiMessages.') || key.startsWith('apiErrors.')) return 'apiDiagnostic'
  // A deliberate leading or trailing space means the string is glued to something else.
  if (value !== value.trim()) return 'fragment'

  const voted = siteList.map(site => site.surface).filter(Boolean)
  if (voted.length > 0) {
    const tally = new Map()
    for (const surface of voted) tally.set(surface, (tally.get(surface) ?? 0) + 1)
    const winner = [...tally].sort((a, b) => b[1] - a[1])[0][0]
    return winner === 'button' && /…$/.test(value) ? 'buttonProgress' : winner
  }

  // Namespace conventions for keys only reached through helpers or lookup tables.
  if (/\.(?:validation|errors|validationError)\./.test(key)) return 'validationError'
  if (/(?:Hint|Help|helpText)$/.test(key)) return 'fieldHint'
  if (/(?:Placeholder|placeholders\.)/.test(key)) return 'placeholder'
  if (/(?:Confirm|confirmDelete|deleteConfirm)/i.test(key)) return 'dialogBody'
  if (/(?:Aria|aria)/.test(key)) return 'ariaLabel'
  if (/\.(?:title|label)$/.test(key)) return 'sectionTitle'
  if (/empty$/i.test(key)) return 'emptyState'
  return 'prose'
}

/** Top-level namespace groups used to split authoring and review into manageable areas. */
export const AREAS = {
  api: ['app', 'apiErrors', 'apiMessages'],
  settings: ['settings'],
  shell: ['shell'],
  accounts: ['accounts', 'tools'],
  schemas: ['reports', 'schemasSubmissions'],
  analytics: ['analytics'],
}

export function areaOf(key) {
  const root = key.split('.')[0]
  for (const [area, roots] of Object.entries(AREAS)) {
    if (roots.includes(root)) return area
  }
  return 'other'
}
