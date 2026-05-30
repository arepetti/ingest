/**
 * Small wrapper around `window.confirm` for destructive actions. Centralised so the prompt
 * style stays consistent across the app and so we have a single seam to swap in a Fluent
 * UI dialog later if "native browser modal" turns out to be too plain.
 *
 * Returns the user's decision (`true` = go ahead, `false` = cancel).
 */
export function confirmDelete(noun: string, target?: string | null): boolean {
  const subject = target?.trim() ? `${noun} "${target.trim()}"` : noun
  return window.confirm(`Delete ${subject}?\n\nThis cannot be undone.`)
}
