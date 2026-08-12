import i18n from '../i18n'

const STORAGE_KEY = 'ingest.apiKey'

export function getApiKey(): string | null {
  return localStorage.getItem(STORAGE_KEY)
}

export function setApiKey(value: string | null) {
  if (value) localStorage.setItem(STORAGE_KEY, value)
  else localStorage.removeItem(STORAGE_KEY)
}

export interface ApiDiagnostic {
  code: string
  message?: string
  params: Record<string, unknown>
}

interface ApiErrorOptions {
  title?: string
  code?: string
  params?: Record<string, unknown>
  errorDetails?: ApiDiagnostic[]
}

export class ApiError extends Error {
  status: number
  title: string
  /**
   * Human-readable summary, ready to drop into a `MessageBar`. When the server returned a list
   * of validation errors they're joined with newlines (one per line) — render with
   * `white-space: pre-line` to preserve the layout.
   */
  detail: string
  /**
   * Individual validation errors when the server reported any (either as our own array shape
   * or as ASP.NET's `{ field: [msg] }` dictionary). Empty array when the response wasn't a
   * validation failure or didn't carry per-error details.
   */
  errors: string[]
  code?: string
  params: Record<string, unknown>
  errorDetails: ApiDiagnostic[]

  constructor(status: number, detail: string, errors: string[] = [], options: ApiErrorOptions = {}) {
    super(`${status} ${detail}`)
    this.name = 'ApiError'
    this.status = status
    this.title = options.title ?? ''
    this.detail = detail
    this.errors = errors
    this.code = options.code
    this.params = options.params ?? {}
    this.errorDetails = options.errorDetails ?? []
  }
}

const diagnosticKey = (code: string) => `apiMessages.${code}`

function genericError(): string {
  return i18n.t('apiErrors.unexpected')
}

/**
 * Localize a structured server diagnostic with the currently active i18n language. Unknown codes
 * never leak their machine identifier: the paired legacy string, diagnostic message, or a generic
 * localized fallback is used instead.
 */
export function localizeDiagnostic(
  diagnostic: ApiDiagnostic | null | undefined,
  legacyFallback?: string | null,
): string {
  const fallback = legacyFallback?.trim() || diagnostic?.message?.trim() || genericError()
  if (!diagnostic?.code || !i18n.isInitialized) return fallback

  const key = diagnosticKey(diagnostic.code)
  if (!i18n.exists(key, { lng: i18n.resolvedLanguage ?? i18n.language })) return fallback

  const localized = i18n.t(key, {
    ...diagnostic.params,
    message: fallback,
  })
  return /\{\{\s*[^}]+\s*\}\}/.test(localized) ? fallback : localized
}

/**
 * Localize a details array while preserving compatibility strings. The arrays are contractually
 * parallel, but old/mixed servers can return different lengths, so unmatched entries from either
 * side remain visible.
 */
export function localizeDiagnostics(
  details: readonly ApiDiagnostic[] | null | undefined,
  legacyMessages: readonly string[] | null | undefined = [],
): string[] {
  const structured = details ?? []
  const legacy = (legacyMessages ?? []).map(message =>
    typeof message === 'string' ? message.trim() : '')
  if (structured.length === 0) return legacy.filter(message => message.length > 0)

  const localized: string[] = []
  const length = Math.max(structured.length, legacy.length)
  for (let index = 0; index < length; index++) {
    const diagnostic = structured[index]
    const fallback = legacy[index]
    if (diagnostic) localized.push(localizeDiagnostic(diagnostic, fallback))
    else if (fallback) localized.push(fallback)
  }
  return localized
}

/**
 * Format an unknown error for direct display in a `MessageBar`. HTTP status codes mean nothing
 * to end users, so we translate them to plain-English summaries and never surface the bare
 * number; when the server returned actionable per-error messages, those alone are shown (the
 * "Validation failed" status code is implied by the message bar's intent).
 *
 * Pair with `white-space: pre-line` (see `AutoScrollMessageBar`) so the newline-separated list
 * actually renders as a list.
 */
export function formatApiError(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.errorDetails.length > 0) {
      return localizeDiagnostics(e.errorDetails, e.errors).join('\n')
    }
    if (e.code) {
      return localizeDiagnostic({ code: e.code, message: e.detail, params: e.params }, e.detail)
    }
    // When the server told us *what* is wrong with specific errors, those are all the user
    // needs to act on — the generic "your request was invalid" prefix would just be noise.
    if (e.errors.length > 0) return e.errors.join('\n')
    const summary = statusSummary(e.status)
    // Only append the server's free-form detail when it adds something on top of the summary
    // (avoids "The request was invalid. — Validation failed." style duplication).
    const detail = e.detail?.trim()
    return detail && !sameMeaning(detail, summary) ? `${summary} — ${detail}` : summary
  }
  if (e instanceof Error) return e.message || genericError()
  return genericError()
}

/**
 * Plain-English summary of an HTTP status. Picked for usefulness to a non-technical reader:
 * what likely happened, and (where the user can do something about it) a hint at the fix.
 */
function statusSummary(status: number): string {
  const key = [400, 401, 403, 404, 408, 409, 410, 413, 415, 422, 429, 500, 502, 503, 504]
    .includes(status)
    ? String(status)
    : status >= 500 ? 'server'
      : status >= 400 ? 'request'
        : 'unexpected'
  return i18n.t(`apiErrors.status.${key}`)
}

// Cheap "are these two strings saying basically the same thing?" check used to avoid pairing
// a status summary with a `detail` that just repeats it (e.g. ASP.NET's default 401 detail is
// "Unauthorized" which adds nothing). Case- and punctuation-insensitive substring match.
function sameMeaning(a: string, b: string): boolean {
  const norm = (x: string) => x.toLowerCase().replace(/[.!?…\s—-]+/g, ' ').trim()
  const na = norm(a)
  const nb = norm(b)
  return na === nb || nb.includes(na) || na.includes(nb)
}

/**
 * Extract every validation error from a parsed ProblemDetails body. Handles two shapes:
 *
 *  - Our `ValidationException` mapping → `{ "errors": ["msg1", "msg2"] }`.
 *  - ASP.NET's automatic DTO model-binding → `{ "errors": { "field": ["msg1", "msg2"] } }`.
 *
 * Anything else (missing, empty, wrong shape) returns an empty array so the caller can fall
 * back to `detail`/`title`.
 */
function extractErrors(parsed: unknown): string[] {
  if (!parsed || typeof parsed !== 'object') return []
  const errors = (parsed as { errors?: unknown }).errors
  if (Array.isArray(errors)) {
    return errors.filter((e): e is string => typeof e === 'string' && e.length > 0)
  }
  if (errors && typeof errors === 'object') {
    const out: string[] = []
    for (const [field, msgs] of Object.entries(errors as Record<string, unknown>)) {
      const prefix = field && field !== '$' ? `${field}: ` : ''
      if (Array.isArray(msgs)) {
        for (const m of msgs) {
          if (typeof m === 'string' && m) out.push(prefix + m)
        }
      } else if (typeof msgs === 'string' && msgs) {
        out.push(prefix + msgs)
      }
    }
    return out
  }
  return []
}

function extractParams(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return {}
  return { ...(value as Record<string, unknown>) }
}

function extractDiagnostic(value: unknown, fallbackMessage = ''): ApiDiagnostic | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const record = value as Record<string, unknown>
  const code = typeof record.code === 'string' ? record.code.trim() : ''
  if (!code) return undefined
  const message = typeof record.message === 'string' && record.message.trim()
    ? record.message
    : fallbackMessage || undefined
  return { code, message, params: extractParams(record.params) }
}

function extractDiagnostics(value: unknown): ApiDiagnostic[] {
  if (!Array.isArray(value)) return []
  return value
    .map(item => extractDiagnostic(item))
    .filter((item): item is ApiDiagnostic => item !== undefined)
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  const key = getApiKey()
  if (key) headers['X-Api-Key'] = key
  // Everything the admin console does is a human action at a keyboard. Tag it so the approval
  // workflow can apply source-aware policies (manual vs. API). Harmless on non-submission calls.
  headers['X-Ingest-Source'] = 'manual'
  const res = await fetch(url, {
    method,
    headers,
    // Send the SSO session cookie when present. Same-origin in prod and through the Vite dev
    // proxy, so this is safe and a no-op for the API-key-only path.
    credentials: 'include',
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    let detail = res.statusText
    let errors: string[] = []
    let title = ''
    let code: string | undefined
    let params: Record<string, unknown> = {}
    let errorDetails: ApiDiagnostic[] = []
    try {
      const text = await res.text()
      if (text) {
        try {
          const parsed = JSON.parse(text) as Record<string, unknown>
          errors = extractErrors(parsed)
          title = typeof parsed.title === 'string' ? parsed.title : ''
          errorDetails = extractDiagnostics(parsed.errorDetails)
          const topDiagnostic = extractDiagnostic(parsed)
          code = topDiagnostic?.code
          params = topDiagnostic?.params ?? {}
          // Validation errors take priority: they're the actionable bit the user needs to see.
          // The generic `title` ("Validation failed", "One or more validation errors occurred.")
          // and `detail` (often just a duplicate of the title) are kept as the fallback for
          // non-validation problem responses.
          if (errors.length > 0) {
            detail = errors.join('\n')
          } else {
            const d = typeof parsed.detail === 'string' ? parsed.detail : ''
            detail = d || title || text
          }
        } catch {
          detail = text
        }
      }
    } catch { /* ignore */ }
    throw new ApiError(res.status, detail, errors, { title, code, params, errorDetails })
  }
  if (res.status === 204) return undefined as T
  const text = await res.text()
  return text ? (JSON.parse(text) as T) : (undefined as T)
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T>(url: string, body?: unknown) => request<T>('POST', url, body),
  put: <T>(url: string, body?: unknown) => request<T>('PUT', url, body),
  delete: <T>(url: string) => request<T>('DELETE', url),
}
