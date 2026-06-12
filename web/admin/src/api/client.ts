const STORAGE_KEY = 'ingest.apiKey'

export function getApiKey(): string | null {
  return localStorage.getItem(STORAGE_KEY)
}

export function setApiKey(value: string | null) {
  if (value) localStorage.setItem(STORAGE_KEY, value)
  else localStorage.removeItem(STORAGE_KEY)
}

export class ApiError extends Error {
  status: number
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
  constructor(status: number, detail: string, errors: string[] = []) {
    super(`${status} ${detail}`)
    this.status = status
    this.detail = detail
    this.errors = errors
  }
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
    // When the server told us *what* is wrong with specific errors, those are all the user
    // needs to act on — the generic "your request was invalid" prefix would just be noise.
    if (e.errors.length > 0) return e.errors.join('\n')
    const summary = statusSummary(e.status)
    // Only append the server's free-form detail when it adds something on top of the summary
    // (avoids "The request was invalid. — Validation failed." style duplication).
    const detail = e.detail?.trim()
    return detail && !sameMeaning(detail, summary) ? `${summary} — ${detail}` : summary
  }
  if (e instanceof Error) return e.message || 'Something went wrong.'
  return String(e)
}

/**
 * Plain-English summary of an HTTP status. Picked for usefulness to a non-technical reader:
 * what likely happened, and (where the user can do something about it) a hint at the fix.
 */
function statusSummary(status: number): string {
  switch (status) {
    case 400: return 'The request was invalid.'
    case 401: return "Your API key wasn't accepted."
    case 403: return "You don't have permission to do this."
    case 404: return 'That item could not be found.'
    case 408: return 'The request took too long. Please try again.'
    case 409: return 'This conflicts with another change. Reload and try again.'
    case 410: return 'That item is no longer available.'
    case 413: return 'The request is too large.'
    case 415: return 'That type of content is not supported.'
    case 422: return 'The data could not be processed.'
    case 429: return 'Too many requests — please wait a moment and try again.'
    case 500: return 'Something went wrong on the server. Please try again.'
    case 502:
    case 503:
    case 504: return "The server isn't responding right now. Please try again shortly."
  }
  if (status >= 500) return 'Something went wrong on the server. Please try again.'
  if (status >= 400) return 'The request failed.'
  return 'Something unexpected happened.'
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

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  const key = getApiKey()
  if (key) headers['X-Api-Key'] = key
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
    try {
      const text = await res.text()
      if (text) {
        try {
          const parsed = JSON.parse(text) as Record<string, unknown>
          errors = extractErrors(parsed)
          // Validation errors take priority: they're the actionable bit the user needs to see.
          // The generic `title` ("Validation failed", "One or more validation errors occurred.")
          // and `detail` (often just a duplicate of the title) are kept as the fallback for
          // non-validation problem responses.
          if (errors.length > 0) {
            detail = errors.join('\n')
          } else {
            const d = typeof parsed.detail === 'string' ? parsed.detail : ''
            const t = typeof parsed.title === 'string' ? parsed.title : ''
            detail = d || t || text
          }
        } catch {
          detail = text
        }
      }
    } catch { /* ignore */ }
    throw new ApiError(res.status, detail, errors)
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
