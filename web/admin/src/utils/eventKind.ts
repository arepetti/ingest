/**
 * Friendly label for an `EventKind`, shared by the Events page, its avatar and the Explore Trend
 * chart's event overlay. Kept out of `components/Avatars.tsx` (a component-only module) so this
 * plain function doesn't break that file's Fast Refresh eligibility.
 */
import type { EventKind } from '../api/types'
import type { TFunction } from 'i18next'
import i18n from '../i18n'

const EVENT_KIND_FALLBACKS: Record<EventKind, string> = {
  PointInTime: 'Point in time',
  Interval: 'Interval',
  FromNowOn: 'From now on',
}

export function eventKindLabel(kind: EventKind, t?: TFunction): string {
  return (t ?? (i18n.isInitialized ? i18n.t : undefined))?.(`shell.eventKind.${kind}`)
    ?? EVENT_KIND_FALLBACKS[kind]
}
