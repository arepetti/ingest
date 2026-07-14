/**
 * Friendly label for an `EventKind`, shared by the Events page, its avatar and the Explore Trend
 * chart's event overlay. Kept out of `components/Avatars.tsx` (a component-only module) so this
 * plain function doesn't break that file's Fast Refresh eligibility.
 */
import type { EventKind } from '../api/types'

const EVENT_KIND_LABELS: Record<EventKind, string> = {
  PointInTime: 'Point in time',
  Interval: 'Interval',
  FromNowOn: 'From now on',
}

export function eventKindLabel(kind: EventKind): string {
  return EVENT_KIND_LABELS[kind]
}
