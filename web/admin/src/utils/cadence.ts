/**
 * Small helpers for cadence-based time math used by the schema editor and the "New" tag rule.
 *
 * The server-side `CadenceCalculator` aligns submissions to bucket boundaries — we don't need
 * that level of precision here; this module only ever asks "is this point still within one
 * cadence period of that other point?" so a sliding-window calendar-aware add is enough.
 */
import type { Cadence } from '../api/types'
import type { TFunction } from 'i18next'
import i18n from '../i18n'

/**
 * Friendly display label for a cadence value. The wire-format names match the C# enum members
 * verbatim, so the only one that benefits from prettification is `SemiAnnually` (rendered as
 * "Semi-annually" to read like natural English). Everything else is already the right adjective.
 */
const CADENCE_FALLBACKS: Record<Cadence, string> = {
  Daily: 'Daily',
  Weekly: 'Weekly',
  Fortnightly: 'Fortnightly',
  Monthly: 'Monthly',
  Quarterly: 'Quarterly',
  SemiAnnually: 'Semi-annually',
  Yearly: 'Yearly',
}

export function cadenceLabel(c: Cadence, t?: TFunction): string {
  return (t ?? (i18n.isInitialized ? i18n.t : undefined))?.(`shell.cadence.${c}`)
    ?? CADENCE_FALLBACKS[c]
}

/**
 * Return a new Date that is `cadence` ahead of `from`. Calendar-aware for Monthly/Yearly
 * (so adding a month to Jan 31 lands on Feb 28/29 like JS's native `setMonth`) and uses fixed
 * day arithmetic for Daily/Weekly.
 */
export function addCadence(from: Date | string, cadence: Cadence): Date {
  const base = typeof from === 'string' ? new Date(from) : new Date(from.getTime())
  switch (cadence) {
    case 'Daily':
      base.setUTCDate(base.getUTCDate() + 1)
      return base
    case 'Weekly':
      base.setUTCDate(base.getUTCDate() + 7)
      return base
    case 'Fortnightly':
      base.setUTCDate(base.getUTCDate() + 14)
      return base
    case 'Monthly':
      base.setUTCMonth(base.getUTCMonth() + 1)
      return base
    case 'Quarterly':
      base.setUTCMonth(base.getUTCMonth() + 3)
      return base
    case 'SemiAnnually':
      base.setUTCMonth(base.getUTCMonth() + 6)
      return base
    case 'Yearly':
      base.setUTCFullYear(base.getUTCFullYear() + 1)
      return base
  }
}

/**
 * True when `now` is strictly within one cadence period after `reference`. Tolerates
 * `reference` being null/undefined (returns false) so callers can pass `schema.versionModifiedAt`
 * directly. A `now` parameter is accepted to keep the helper deterministic in tests.
 */
export function isWithinOneCadenceOf(
  reference: Date | string | null | undefined,
  cadence: Cadence,
  now: Date = new Date(),
): boolean {
  if (!reference) return false
  const ref = typeof reference === 'string' ? new Date(reference) : reference
  if (Number.isNaN(ref.getTime())) return false
  const end = addCadence(ref, cadence)
  return now.getTime() < end.getTime()
}
