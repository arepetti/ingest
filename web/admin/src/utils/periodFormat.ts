/**
 * Compact, cadence-aware labels for a bucket's `periodStart`, used on chart X axes and in the
 * "view as table" period column. Extracted from the schema history chart so the Explore page and
 * any future time-series view share one implementation. All math is in UTC because bucket
 * boundaries are computed UTC server-side.
 */
import type { Cadence } from '../api/types'

/** Zero-pad a 1- or 2-digit number to two characters (e.g. 3 → "03"). */
export function pad(n: number): string {
  return n < 10 ? `0${n}` : String(n)
}

/** ISO 8601 week-of-year (Monday-based). Used to label weekly/fortnightly buckets. */
export function isoWeek(d: Date): number {
  const target = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()))
  const dayNr = (target.getUTCDay() + 6) % 7
  target.setUTCDate(target.getUTCDate() - dayNr + 3)
  const firstThursday = new Date(Date.UTC(target.getUTCFullYear(), 0, 4))
  const diff = (target.getTime() - firstThursday.getTime()) / 86400000
  return 1 + Math.round((diff - 3 + ((firstThursday.getUTCDay() + 6) % 7)) / 7)
}

/**
 * Format a bucket start (ISO string) into a short label appropriate for its cadence:
 * `2026-03-14` (daily), `2026-W11` (weekly/fortnightly), `2026-03` (monthly), `2026-Q1`
 * (quarterly), `2026-H1` (semi-annually), `2026` (yearly). Falls back to the raw string when the
 * date can't be parsed.
 */
export function formatPeriodLabel(periodStart: string, cadence: Cadence): string {
  const start = new Date(periodStart)
  if (Number.isNaN(start.getTime())) return periodStart
  const y = start.getUTCFullYear()
  const m = start.getUTCMonth() + 1
  const d = start.getUTCDate()
  switch (cadence) {
    case 'Daily':
      return `${y}-${pad(m)}-${pad(d)}`
    case 'Weekly':
    case 'Fortnightly':
      return `${y}-W${pad(isoWeek(start))}`
    case 'Monthly':
      return `${y}-${pad(m)}`
    case 'Quarterly':
      return `${y}-Q${Math.floor((m - 1) / 3) + 1}`
    case 'SemiAnnually':
      return `${y}-${m <= 6 ? 'H1' : 'H2'}`
    case 'Yearly':
      return `${y}`
    default:
      return start.toISOString()
  }
}
