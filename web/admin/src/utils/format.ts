/** Format an ISO timestamp as a locale date (no time). Returns an em-dash for empty/invalid input. */
export function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString()
}

/** Format an ISO timestamp as a full locale date + time. Returns an em-dash for empty/invalid input. */
export function formatDateTime(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString()
}
