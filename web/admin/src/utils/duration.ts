/**
 * Flexible parsing/formatting for the Events page's Duration field. Users can type whichever shape
 * is most natural for the length involved — plain minutes for anything short, `HH:mm` once hours
 * are involved, or `dd HH:mm` once it spans multiple days — and it's always normalised to whole
 * minutes on the wire (`UpsertEventRequest.durationMinutes`).
 */

const MINUTES_ONLY = /^(\d+)$/
const HOURS_MINUTES = /^(\d+):([0-5]?\d)$/
const DAYS_HOURS_MINUTES = /^(\d+)\s+(\d+):([0-5]?\d)$/

/**
 * Parse a duration string in one of three formats into whole minutes:
 * - `mmm` — plain minutes, e.g. `90`.
 * - `HH:mm` — hours and minutes, e.g. `1:30` (90 minutes).
 * - `dd HH:mm` — days, hours and minutes, e.g. `2 03:15` (2 days, 3h15m).
 *
 * Returns `null` for an empty/blank input or one that matches none of the three shapes.
 */
export function parseDurationMinutes(input: string): number | null {
  const s = input.trim()
  if (!s) return null

  const dhm = DAYS_HOURS_MINUTES.exec(s)
  if (dhm) return Number(dhm[1]) * 1440 + Number(dhm[2]) * 60 + Number(dhm[3])

  const hm = HOURS_MINUTES.exec(s)
  if (hm) return Number(hm[1]) * 60 + Number(hm[2])

  const mm = MINUTES_ONLY.exec(s)
  if (mm) return Number(mm[1])

  return null
}

/**
 * Format whole minutes back into the most readable of the three input shapes, so re-opening an
 * existing event doesn't show e.g. `1590` when `1 02:30` is what a person would have typed.
 * Under an hour stays plain minutes; under a day becomes `H:mm`; otherwise `d H:mm`.
 */
export function formatDurationInput(totalMinutes: number): string {
  if (!Number.isFinite(totalMinutes) || totalMinutes < 60) return String(Math.max(0, Math.trunc(totalMinutes)))
  const whole = Math.trunc(totalMinutes)
  const days = Math.floor(whole / 1440)
  const hours = Math.floor((whole % 1440) / 60)
  const minutes = whole % 60
  const hm = `${hours}:${String(minutes).padStart(2, '0')}`
  return days > 0 ? `${days} ${hm}` : hm
}
