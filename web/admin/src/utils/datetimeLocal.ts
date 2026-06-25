// Helpers for `<input type="datetime-local">`, which works in local time with no timezone, while the
// rest of the app stores instants as UTC ISO strings. These round-trip between the two.

/** Format a UTC ISO instant as the local 'YYYY-MM-DDTHH:mm' string the input expects. */
export function toLocalInput(iso: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Parse a local 'YYYY-MM-DDTHH:mm' input value back into a UTC ISO instant. */
export function fromLocalInput(local: string): string {
  if (!local) return ''
  const d = new Date(local)
  return d.toISOString()
}
