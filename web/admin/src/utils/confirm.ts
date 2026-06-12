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
export function confirmDelete(noun: string, target?: string | null, note?: string): boolean {
  const subject = target?.trim() ? `${noun} "${target.trim()}"` : noun
  const tail = note?.trim() ? note.trim() : 'This cannot be undone.'
  return window.confirm(`Delete ${subject}?\n\n${tail}`)
}
