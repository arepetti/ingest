/**
 * Client-side runtime for validation expressions. Two responsibilities:
 *
 *  1. Translation client: ask the server (`POST /api/expressions/translate`) to compile a
 *     rule's source text into an equivalent JavaScript expression, then wrap it in a
 *     `Function` so the editor can evaluate it on every keystroke. Results are cached for
 *     the lifetime of the page — schema rules are immutable while the page is open, so each
 *     unique rule is translated exactly once.
 *  2. Runtime helpers: the `H` namespace the translator's emitted JS expects. It implements
 *     the null-handling, type-coercion, and built-in functions that the server-side NCalc
 *     evaluator uses, so what users see in the editor matches what they'd get if they
 *     posted the submission.
 *
 * Server-side validation remains authoritative — if translation fails or a rule references
 * something the runtime doesn't know about, the editor falls back to "show + no warning" so
 * data never silently disappears.
 */
import { getApiKey } from '../api/client'

/** Media type the translator endpoint is asked to produce. The server returns the equivalent
 *  JavaScript expression in the response body; we wrap it in a Function on this side. */
const JS_MEDIA_TYPE = 'text/javascript'

/** Raised by `tryEvaluateExpression` when a compiled rule throws at run time. */
export class ExpressionError extends Error {}

type CompiledFn = (V: Record<string, unknown>, H: Helpers) => unknown
type Evaluator = (variables: Record<string, unknown>) => unknown

interface CacheEntry {
  status: 'compiled' | 'failed'
  evaluator?: Evaluator
  error?: string
}

const cache = new Map<string, CacheEntry>()
const pending = new Map<string, Promise<CacheEntry>>()

async function fetchAndCompile(expression: string): Promise<CacheEntry> {
  try {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      // The Accept header is the contract: the server inspects it to decide the target
      // language. Today only JS is supported (server will return 406 for anything else);
      // a future text/plain target is reserved for a human-readable explanation of the rule.
      'Accept': JS_MEDIA_TYPE,
    }
    // The endpoint is anonymous, but if a key is in storage we still forward it so the
    // request follows the same auth path as every other call.
    const apiKey = getApiKey()
    if (apiKey) headers['X-Api-Key'] = apiKey

    const resp = await fetch('/api/expressions/translate', {
      method: 'POST',
      headers,
      body: JSON.stringify({ expression }),
    })
    if (!resp.ok) {
      const detail = await safeReadProblemDetail(resp)
      const entry: CacheEntry = { status: 'failed', error: detail || `HTTP ${resp.status}` }
      cache.set(expression, entry)
      return entry
    }
    const js = await resp.text()
    // The translator only emits a closed grammar of safe operations: helper calls, ternary
    // branches, literals, and bracket-notation variable lookups. Wrapping in a fresh Function
    // gives us a sandbox without globals beyond V and H.
    const fn = new Function('V', 'H', `"use strict"; return (${js})`) as CompiledFn
    const entry: CacheEntry = {
      status: 'compiled',
      evaluator: (vars) => fn(vars, helpers),
    }
    cache.set(expression, entry)
    return entry
  } catch (e) {
    const entry: CacheEntry = { status: 'failed', error: String(e) }
    cache.set(expression, entry)
    return entry
  }
}

async function safeReadProblemDetail(resp: Response): Promise<string | null> {
  // Errors come back as RFC 7807 problem+json; surface the title/detail when we can so the
  // editor's diagnostics tell the user what went wrong instead of a bare status code.
  try {
    const text = await resp.text()
    if (!text) return null
    try {
      const parsed = JSON.parse(text)
      return parsed.detail ?? parsed.title ?? text
    } catch {
      return text
    }
  } catch {
    return null
  }
}

/**
 * Translate `expression` (or return the cached compilation). Resolves with a
 * `CacheEntry` regardless of outcome; inspect `status` to tell success from failure.
 */
export function translateExpression(expression: string): Promise<CacheEntry> {
  const key = expression.trim()
  if (!key) return Promise.resolve({ status: 'failed', error: 'Expression is empty.' })
  const hit = cache.get(key)
  if (hit) return Promise.resolve(hit)
  const inflight = pending.get(key)
  if (inflight) return inflight
  const p = fetchAndCompile(key).finally(() => { pending.delete(key) })
  pending.set(key, p)
  return p
}

/**
 * Translate a batch of expressions concurrently. Duplicates and empty strings are skipped.
 * Returns when all translations have settled (either compiled or failed) so callers can flip
 * a re-render flag immediately after.
 */
export async function prefetchExpressions(expressions: Iterable<string | null | undefined>): Promise<void> {
  const seen = new Set<string>()
  const promises: Promise<unknown>[] = []
  for (const e of expressions) {
    const k = (e ?? '').trim()
    if (!k || seen.has(k)) continue
    seen.add(k)
    promises.push(translateExpression(k))
  }
  await Promise.all(promises)
}

/**
 * Synchronous best-effort evaluation. Returns `undefined` when the expression has not
 * been translated yet (or translation failed), so callers can treat "no decision yet" the
 * same as "no rule".
 */
export function tryEvaluateExpression(
  expression: string,
  variables: Record<string, unknown>,
): unknown | undefined {
  const key = expression.trim()
  const entry = cache.get(key)
  if (!entry || entry.status !== 'compiled' || !entry.evaluator) return undefined
  try {
    return entry.evaluator(variables)
  } catch (e) {
    throw new ExpressionError(String(e))
  }
}

/** Truthiness rules used by the runtime, mirrored from the server. Exported because UI
 *  code sometimes needs the same check (e.g. to interpret a Warning rule's result). */
export function isTruthy(value: unknown): boolean {
  if (value === null || value === undefined) return false
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0 && !Number.isNaN(value)
  if (typeof value === 'string') return value.length > 0
  if (Array.isArray(value)) return value.length > 0
  return true
}

// ---------------------------------------------------------------------------
// Helpers runtime (the `H` object the translated JS expects).
// ---------------------------------------------------------------------------

type Helpers = typeof helpers

function toNumber(value: unknown): number | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'number') return Number.isFinite(value) ? value : null
  if (typeof value === 'boolean') return value ? 1 : 0
  if (typeof value === 'string') {
    const t = value.trim()
    if (!t) return null
    const n = Number(t)
    return Number.isNaN(n) ? null : n
  }
  if (value instanceof Date) return value.getTime()
  return null
}

function toDate(value: unknown): Date | null {
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value
  if (typeof value === 'string') {
    const d = new Date(value)
    return Number.isNaN(d.getTime()) ? null : d
  }
  if (typeof value === 'number' && Number.isFinite(value)) return new Date(value)
  return null
}

function looseEq(a: unknown, b: unknown): boolean {
  if (a === b) return true
  if (a === null || a === undefined) return b === null || b === undefined
  if (b === null || b === undefined) return false
  if (a instanceof Date || b instanceof Date) {
    const da = toDate(a); const db = toDate(b)
    return !!da && !!db && da.getTime() === db.getTime()
  }
  if (typeof a === 'number' || typeof b === 'number') {
    const na = toNumber(a); const nb = toNumber(b)
    return na !== null && nb !== null && na === nb
  }
  return String(a) === String(b)
}

function compareValues(a: unknown, b: unknown): number | null {
  if (a === null || a === undefined || b === null || b === undefined) return null
  if (a instanceof Date || b instanceof Date) {
    const da = toDate(a); const db = toDate(b)
    if (!da || !db) return null
    return da.getTime() - db.getTime()
  }
  if (typeof a === 'string' && typeof b === 'string') {
    return a < b ? -1 : a > b ? 1 : 0
  }
  const na = toNumber(a); const nb = toNumber(b)
  if (na !== null && nb !== null) return na - nb
  return null
}

function likeMatch(value: unknown, pattern: unknown): boolean {
  const s = value === null || value === undefined ? '' : String(value)
  const p = pattern === null || pattern === undefined ? '' : String(pattern)
  // SQL LIKE: % matches any sequence, _ matches a single char. Escape the rest as literal.
  const re = p.replace(/[\\^$.*+?()[\]{}|]/g, '\\$&')
    .replace(/%/g, '.*')
    .replace(/_/g, '.')
  return new RegExp('^' + re + '$').test(s)
}

function callBuiltin(name: string, args: unknown[]): unknown {
  switch (name.toLowerCase()) {
    case 'isnull': return args.length === 0 || args[0] === null || args[0] === undefined
    case 'coalesce':
      for (const a of args) if (a !== null && a !== undefined) return a
      return null
    case 'len': {
      const v = args[0]
      if (typeof v === 'string') return v.length
      if (Array.isArray(v)) return v.length
      return 0
    }
    case 'not': return !isTruthy(args[0])
    case 'in': {
      const [val, ...rest] = args
      return rest.some(r => looseEq(val, r))
    }
    case 'now': return new Date()
    case 'today': {
      const d = new Date()
      d.setUTCHours(0, 0, 0, 0)
      return d
    }
    case 'dayofweek':  { const d = toDate(args[0]); return d ? d.getUTCDay() : null }
    case 'dayofmonth': { const d = toDate(args[0]); return d ? d.getUTCDate() : null }
    case 'dayofyear': {
      const d = toDate(args[0]); if (!d) return null
      const start = Date.UTC(d.getUTCFullYear(), 0, 0)
      return Math.floor((d.getTime() - start) / 86_400_000)
    }
    case 'weekofyear': {
      // ISO 8601 week number, matches .NET's ISOWeek.GetWeekOfYear.
      const d = toDate(args[0]); if (!d) return null
      const target = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()))
      const dayNr = (target.getUTCDay() + 6) % 7
      target.setUTCDate(target.getUTCDate() - dayNr + 3)
      const firstThursday = new Date(Date.UTC(target.getUTCFullYear(), 0, 4))
      const offset = ((firstThursday.getUTCDay() + 6) % 7)
      return 1 + Math.round(((target.getTime() - firstThursday.getTime()) / 86_400_000 - 3 + offset) / 7)
    }
    case 'month':  { const d = toDate(args[0]); return d ? d.getUTCMonth() + 1 : null }
    case 'year':   { const d = toDate(args[0]); return d ? d.getUTCFullYear() : null }
    case 'hour':   { const d = toDate(args[0]); return d ? d.getUTCHours() : null }
    case 'minute': { const d = toDate(args[0]); return d ? d.getUTCMinutes() : null }
    case 'second': { const d = toDate(args[0]); return d ? d.getUTCSeconds() : null }
    case 'average': {
      let sum = 0
      let count = 0
      for (const a of args) {
        if (a === null || a === undefined) continue
        if (typeof a === 'boolean') { sum += a ? 1 : 0; count++; continue }
        if (typeof a === 'number') {
          if (!Number.isFinite(a)) throw new Error(`average() expects numeric or boolean arguments, got number.`)
          sum += a
          count++
          continue
        }
        throw new Error(`average() expects numeric or boolean arguments, got ${typeof a}.`)
      }
      return count === 0 ? null : sum / count
    }
    case 'higher_than': {
      // Mirrors NCalcExpressionEvaluator.HigherThan: true when value exceeds reference by more
      // than percentage% (percentage as a whole number, e.g. 50 == 50%). Null args ⇒ false.
      if (args.length < 3) throw new Error('higher_than() expects (value, reference, percentage).')
      if (args[0] === null || args[0] === undefined ||
          args[1] === null || args[1] === undefined ||
          args[2] === null || args[2] === undefined) return false
      const value = toNumber(args[0])
      const reference = toNumber(args[1])
      const percentage = toNumber(args[2])
      if (value === null || reference === null || percentage === null)
        throw new Error('higher_than() expects numeric arguments.')
      return value > reference * (1 + percentage / 100)
    }
    case 'latest':
    case 'previous': {
      // Historical values aren't available in the browser preview — the server resolves them
      // from the last live submission at validation time. Honour the optional fallback default
      // (2nd argument when a value name is given, 1st when called for the current value), else
      // return null so the preview stays permissive (never rejects on data it can't see).
      if (args.length > 0 && typeof args[0] === 'string') return args.length > 1 ? args[1] : null
      return args.length > 0 ? args[0] : null
    }
    default:
      // Unknown function — return null so the rule evaluates falsy (no warning, no hide).
      // The server is the source of truth and will catch real issues.
      return null
  }
}

const helpers = {
  /** Case-insensitive variable lookup. Missing keys return `null`, matching the server. */
  var(V: Record<string, unknown>, name: string): unknown {
    const lower = name.toLowerCase()
    for (const k of Object.keys(V)) {
      if (k.toLowerCase() === lower) {
        const v = V[k]
        return v === undefined ? null : v
      }
    }
    return null
  },
  bool: (v: unknown) => isTruthy(v),
  neg: (v: unknown) => { const n = toNumber(v); return n === null ? null : -n },
  add(a: unknown, b: unknown): unknown {
    if (a === null || a === undefined || b === null || b === undefined) return null
    if (typeof a === 'string' || typeof b === 'string') return String(a) + String(b)
    const na = toNumber(a); const nb = toNumber(b)
    return na !== null && nb !== null ? na + nb : null
  },
  sub(a: unknown, b: unknown): unknown {
    const na = toNumber(a); const nb = toNumber(b)
    return na !== null && nb !== null ? na - nb : null
  },
  mul(a: unknown, b: unknown): unknown {
    const na = toNumber(a); const nb = toNumber(b)
    return na !== null && nb !== null ? na * nb : null
  },
  div(a: unknown, b: unknown): unknown {
    const na = toNumber(a); const nb = toNumber(b)
    if (na === null || nb === null || nb === 0) return null
    return na / nb
  },
  mod(a: unknown, b: unknown): unknown {
    const na = toNumber(a); const nb = toNumber(b)
    if (na === null || nb === null || nb === 0) return null
    return na % nb
  },
  pow(a: unknown, b: unknown): unknown {
    const na = toNumber(a); const nb = toNumber(b)
    return na !== null && nb !== null ? Math.pow(na, nb) : null
  },
  eq: (a: unknown, b: unknown) => looseEq(a, b),
  neq: (a: unknown, b: unknown) => !looseEq(a, b),
  gt:  (a: unknown, b: unknown) => { const c = compareValues(a, b); return c === null ? null : c > 0 },
  gte: (a: unknown, b: unknown) => { const c = compareValues(a, b); return c === null ? null : c >= 0 },
  lt:  (a: unknown, b: unknown) => { const c = compareValues(a, b); return c === null ? null : c < 0 },
  lte: (a: unknown, b: unknown) => { const c = compareValues(a, b); return c === null ? null : c <= 0 },
  bitAnd: (a: unknown, b: unknown) => { const na = toNumber(a); const nb = toNumber(b); return na !== null && nb !== null ? (na | 0) & (nb | 0) : null },
  bitOr:  (a: unknown, b: unknown) => { const na = toNumber(a); const nb = toNumber(b); return na !== null && nb !== null ? (na | 0) | (nb | 0) : null },
  bitXor: (a: unknown, b: unknown) => { const na = toNumber(a); const nb = toNumber(b); return na !== null && nb !== null ? (na | 0) ^ (nb | 0) : null },
  bitNot: (a: unknown) => { const na = toNumber(a); return na === null ? null : ~(na | 0) },
  shl:    (a: unknown, b: unknown) => { const na = toNumber(a); const nb = toNumber(b); return na !== null && nb !== null ? (na | 0) << (nb | 0) : null },
  shr:    (a: unknown, b: unknown) => { const na = toNumber(a); const nb = toNumber(b); return na !== null && nb !== null ? (na | 0) >> (nb | 0) : null },
  fact(v: unknown): unknown {
    const n = toNumber(v)
    if (n === null || n < 0 || Math.floor(n) !== n) return null
    let r = 1
    for (let i = 2; i <= n; i++) r *= i
    return r
  },
  like: likeMatch,
  notLike: (a: unknown, b: unknown) => !likeMatch(a, b),
  in: (value: unknown, arr: unknown[]) => arr.some(item => looseEq(value, item)),
  date: (iso: string) => new Date(iso),
  call: callBuiltin,
}

// ---------------------------------------------------------------------------
// Syntax-only validation client (`POST /api/expressions/validate`).
// Separate cache from the translator: a successful translation also implies a passing syntax
// check, but the reverse isn't guaranteed (some grammars parse but error on unsupported
// constructs at translation time), so we keep the caches independent for clarity.
// ---------------------------------------------------------------------------

/** Outcome of a syntax-only check. `ok: true` means the parser accepted the expression. */
export type ExpressionSyntaxResult =
  | { ok: true }
  | { ok: false; error: string; position?: number }

const syntaxCache = new Map<string, ExpressionSyntaxResult>()
const syntaxPending = new Map<string, Promise<ExpressionSyntaxResult>>()

async function fetchValidate(expression: string): Promise<ExpressionSyntaxResult> {
  try {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    }
    const apiKey = getApiKey()
    if (apiKey) headers['X-Api-Key'] = apiKey

    const resp = await fetch('/api/expressions/validate', {
      method: 'POST',
      headers,
      body: JSON.stringify({ expression }),
    })
    if (!resp.ok) {
      // Protocol errors (empty body, over-length) come back as 4xx — treat them as "we don't
      // know" so the editor doesn't draw a red squiggle for a transport hiccup.
      const detail = await safeReadProblemDetail(resp)
      const result: ExpressionSyntaxResult = { ok: false, error: detail || `HTTP ${resp.status}` }
      syntaxCache.set(expression, result)
      return result
    }
    const body = await resp.json() as { ok: boolean; error?: string; position?: number }
    const result: ExpressionSyntaxResult = body.ok
      ? { ok: true }
      : { ok: false, error: body.error ?? 'Invalid expression.', position: body.position }
    syntaxCache.set(expression, result)
    return result
  } catch (e) {
    const result: ExpressionSyntaxResult = { ok: false, error: String(e) }
    syntaxCache.set(expression, result)
    return result
  }
}

/**
 * Syntactically validate `expression` against the server's parser (or return the cached
 * result). Resolves with `ok: true` when the parser accepted the input; `ok: false` carries an
 * error message suitable for inline rendering. Unknown identifiers / function names are
 * deliberately not flagged — the schema-save round-trip catches those.
 */
export function validateExpression(expression: string): Promise<ExpressionSyntaxResult> {
  const key = expression.trim()
  if (!key) return Promise.resolve({ ok: false, error: 'Expression is empty.' })
  const hit = syntaxCache.get(key)
  if (hit) return Promise.resolve(hit)
  const inflight = syntaxPending.get(key)
  if (inflight) return inflight
  const p = fetchValidate(key).finally(() => { syntaxPending.delete(key) })
  syntaxPending.set(key, p)
  return p
}

/** Test-only escape hatch: wipe both translation and validation caches between assertions. */
export function _clearExpressionCache(): void {
  cache.clear()
  pending.clear()
  syntaxCache.clear()
  syntaxPending.clear()
}

/** Test-only: invoke a built-in function through the same runtime path translated rules use. */
export function _callBuiltin(name: string, args: unknown[]): unknown {
  return callBuiltin(name, args)
}
