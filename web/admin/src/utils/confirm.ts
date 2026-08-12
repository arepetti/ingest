/**
 * Small wrapper around `window.confirm` for destructive actions. Centralised so the prompt
 * style stays consistent across the app and so we have a single seam to swap in a Fluent
 * UI dialog later if "native browser modal" turns out to be too plain.
 *
 * Returns the user's decision (`true` = go ahead, `false` = cancel).
 *
 * `note` overrides the default "This cannot be undone." trailing line when the caller wants to
 * spell out a more specific consequence (e.g. "this may break existing submissions").
 */
import type { TFunction } from 'i18next'
import i18n from '../i18n'

export function confirmDelete(
  noun: string,
  target?: string | null,
  note?: string,
  t?: TFunction,
): boolean {
  const translate = t ?? (i18n.isInitialized ? i18n.t : undefined)
  const trimmedTarget = target?.trim()
  const subject = trimmedTarget
    ? translate?.('shell.confirm.namedSubject', { noun, target: trimmedTarget })
      ?? `${noun} "${trimmedTarget}"`
    : noun
  const tail = note?.trim()
    || translate?.('shell.confirm.cannotUndo')
    || 'This cannot be undone.'
  return window.confirm(
    translate?.('shell.confirm.delete', { subject, note: tail })
      ?? `Delete ${subject}?\n\n${tail}`,
  )
}
