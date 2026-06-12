/**
 * Minimal RFC 4180 CSV builder used by the grids' "Export CSV" buttons. Kept dependency-free and
 * separate from the download helpers so it's trivial to unit-test and reuse. Mirrors the escaping
 * the server-side audit export uses (quote a field only when it contains a comma, quote or line
 * break; double up embedded quotes) and emits CRLF line endings so Excel opens the file cleanly.
 */

type CsvCell = string | number | boolean | null | undefined

function escapeCsvField(value: CsvCell): string {
  if (value === null || value === undefined) return ''
  const text = typeof value === 'boolean' ? (value ? 'true' : 'false') : String(value)
  if (text === '') return ''
  return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

/** Build a CSV document from a header row and a list of data rows. */
export function buildCsv(headers: string[], rows: CsvCell[][]): string {
  const lines = [headers.map(escapeCsvField).join(',')]
  for (const row of rows) lines.push(row.map(escapeCsvField).join(','))
  return lines.join('\r\n')
}
