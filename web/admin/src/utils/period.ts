/**
 * Shared "filter by period" helpers used by list pages that support a relative/custom date range.
 * Ranges are half-open: `from` inclusive, `to` exclusive — matching the server-side filters.
 */

export type Interval = 'all' | 'lastDay' | 'lastWeek' | 'lastMonth' | 'custom'

export const INTERVAL_LABELS: Record<Interval, string> = {
  all: 'All time',
  lastDay: 'Last day',
  lastWeek: 'Last week',
  lastMonth: 'Last month',
  custom: 'Custom range',
}

function addDays(d: Date, days: number): Date {
  const r = new Date(d)
  r.setDate(r.getDate() + days)
  return r
}

/** Convert a `datetime-local` input value to a UTC ISO string, or undefined when blank/invalid. */
function fromLocalInput(local: string): string | undefined {
  if (!local) return undefined
  const d = new Date(local)
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString()
}

/**
 * Resolve a relative/custom interval to a `{ from, to }` pair of UTC ISO strings (either side may be
 * undefined: "all time" yields none, a custom range may be open-ended on one side).
 */
export function intervalRange(
  interval: Interval,
  customFrom: string,
  customTo: string,
): { from?: string; to?: string } {
  const now = new Date()
  switch (interval) {
    case 'all':       return {}
    case 'lastDay':   return { from: addDays(now, -1).toISOString(),  to: now.toISOString() }
    case 'lastWeek':  return { from: addDays(now, -7).toISOString(),  to: now.toISOString() }
    case 'lastMonth': return { from: addDays(now, -30).toISOString(), to: now.toISOString() }
    case 'custom':    return { from: fromLocalInput(customFrom), to: fromLocalInput(customTo) }
  }
}
