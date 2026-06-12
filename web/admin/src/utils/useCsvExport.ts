import { useState } from 'react'
import { buildCsv } from './csv'
import { downloadText } from './download'
import { formatApiError } from '../api/client'

/** One column of the exported CSV: a header label plus an accessor producing the cell value. */
export type ExportColumn<T> = {
  header: string
  value: (item: T) => string | number | boolean | null | undefined
}

/**
 * Shared CSV-export behaviour for the first-level grids. `exportList` fetches the *entire* list via
 * `fetchAll` (not just the page on screen), turns it into a CSV using `columns`, and triggers a
 * browser download. `exporting` is a busy flag callers can use to disable their trigger. Failures
 * are surfaced through `onError` so they can land in the host page's existing message bar.
 */
export function useCsvExport<T>({
  filename,
  columns,
  fetchAll,
  onError,
}: {
  filename: string
  columns: ExportColumn<T>[]
  fetchAll: () => Promise<T[]>
  onError?: (message: string) => void
}): { exportList: () => Promise<void>; exporting: boolean } {
  const [exporting, setExporting] = useState(false)

  async function exportList() {
    setExporting(true)
    try {
      const items = await fetchAll()
      const csv = buildCsv(
        columns.map(c => c.header),
        items.map(item => columns.map(c => c.value(item))),
      )
      downloadText(filename, csv, 'text/csv;charset=utf-8')
    } catch (e) {
      onError?.(formatApiError(e))
    } finally {
      setExporting(false)
    }
  }

  return { exportList, exporting }
}
