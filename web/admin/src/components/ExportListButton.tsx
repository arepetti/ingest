import { Button } from '@fluentui/react-components'
import { ArrowDownload20Regular } from '@fluentui/react-icons'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'

/** Standalone "Export CSV" button, built on {@link useCsvExport}. */
export function ExportListButton<T>({
  filename,
  columns,
  fetchAll,
  onError,
  label = 'Export CSV',
  appearance,
  disabled,
}: {
  filename: string
  columns: ExportColumn<T>[]
  fetchAll: () => Promise<T[]>
  onError?: (message: string) => void
  label?: string
  appearance?: 'primary' | 'secondary' | 'outline' | 'subtle' | 'transparent'
  disabled?: boolean
}) {
  const { exportList, exporting } = useCsvExport({ filename, columns, fetchAll, onError })

  return (
    <Button
      appearance={appearance}
      icon={<ArrowDownload20Regular />}
      disabled={disabled || exporting}
      onClick={exportList}
    >
      {exporting ? 'Exporting…' : label}
    </Button>
  )
}
