import { useState } from 'react'
import {
  Body1, Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle,
  Dropdown, Field, MessageBarBody, Option, Spinner, Text, makeStyles, tokens,
} from '@fluentui/react-components'
import { AutoScrollMessageBar } from './AutoScrollMessageBar'
import { formatApiError } from '../api/client'
import { useBulkImport } from '../api/hooks'
import type { Account, BulkImportFormat, BulkImportResult } from '../api/types'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: '12px' },
  fileRow: { display: 'flex', alignItems: 'center', gap: '8px' },
  report: { display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '4px' },
  itemList: { margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '6px' },
  errors: { margin: '2px 0 0', paddingLeft: '18px', color: tokens.colorPaletteRedForeground1 },
  help: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
})

const formatLabels: Record<BulkImportFormat, string> = { Json: 'JSON', Csv: 'CSV' }

function detectFormat(fileName: string): BulkImportFormat | null {
  const lower = fileName.toLowerCase()
  if (lower.endsWith('.json')) return 'Json'
  if (lower.endsWith('.csv')) return 'Csv'
  return null
}

/**
 * Admin-only dialog that uploads a JSON/CSV file of historical submissions for a single service.
 * The file is read in the browser and its text posted to the import endpoint; the per-group report
 * is shown inline so the admin can see exactly what was imported and fix any rejected groups.
 */
export function BulkImportDialog({
  open, onClose, services,
}: {
  open: boolean
  onClose: () => void
  services: Account[]
}) {
  const s = useStyles()
  const importer = useBulkImport()

  const [serviceId, setServiceId] = useState<string>('')
  const [format, setFormat] = useState<BulkImportFormat>('Json')
  const [content, setContent] = useState<string>('')
  const [result, setResult] = useState<BulkImportResult | null>(null)

  function reset() {
    setServiceId('')
    setFormat('Json')
    setContent('')
    setResult(null)
    importer.reset()
  }

  function handleClose() {
    reset()
    onClose()
  }

  async function onFile(file: File | undefined) {
    setResult(null)
    importer.reset()
    if (!file) { setContent(''); return }
    const detected = detectFormat(file.name)
    if (detected) setFormat(detected)
    setContent(await file.text())
  }

  async function onImport() {
    if (!serviceId || !content) return
    setResult(null)
    const res = await importer.mutateAsync({ serviceAccountId: serviceId, format, content })
    setResult(res)
  }

  const serviceName = (id: string) => {
    const a = services.find(x => x.id === id)
    return a ? (a.label || a.name) : ''
  }
  const canImport = !!serviceId && !!content && !importer.isPending

  return (
    <Dialog open={open} onOpenChange={(_, d) => { if (!d.open) handleClose() }}>
      <DialogSurface style={{ minWidth: 620 }}>
        <DialogBody>
          <DialogTitle>Import submissions</DialogTitle>
          <DialogContent>
            <div className={s.form}>
              <Body1 className={s.help}>
                Import historical submissions for a single service from a JSON or CSV file. Parsing
                must succeed for the whole file; each submission is then validated and saved
                independently, so a rejected one won&apos;t block the rest. Submissions that already
                exist are skipped, so re-running the same file is safe.
              </Body1>

              <Field label="Service" required>
                <Dropdown
                  placeholder="Choose a service"
                  selectedOptions={serviceId ? [serviceId] : []}
                  value={serviceId ? serviceName(serviceId) : ''}
                  onOptionSelect={(_, d) => setServiceId(d.optionValue || '')}
                >
                  {services.map(a => (
                    <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
                  ))}
                </Dropdown>
              </Field>

              <Field label="File" required>
                <div className={s.fileRow}>
                  <input
                    type="file"
                    accept=".json,.csv,application/json,text/csv"
                    onChange={e => onFile(e.target.files?.[0])}
                  />
                </div>
              </Field>

              <Field label="Format" hint="Auto-detected from the file extension; override if needed.">
                <Dropdown
                  selectedOptions={[format]}
                  value={formatLabels[format]}
                  onOptionSelect={(_, d) => setFormat((d.optionValue as BulkImportFormat) ?? 'Json')}
                >
                  {(Object.keys(formatLabels) as BulkImportFormat[]).map(k => (
                    <Option key={k} value={k}>{formatLabels[k]}</Option>
                  ))}
                </Dropdown>
              </Field>

              {importer.isError && (
                <AutoScrollMessageBar intent="error">
                  <MessageBarBody>{formatApiError(importer.error)}</MessageBarBody>
                </AutoScrollMessageBar>
              )}

              {result && (() => {
                const failures = result.items.filter(item => !item.success && !item.skipped)
                return (
                  <div className={s.report}>
                    <AutoScrollMessageBar intent={result.failed === 0 ? 'success' : 'warning'}>
                      <MessageBarBody>
                        Imported {result.succeeded} of {result.total} submission{result.total === 1 ? '' : 's'}.
                        {result.skipped > 0 ? ` ${result.skipped} already existed (skipped).` : ''}
                        {result.failed > 0 ? ` ${result.failed} failed.` : ''}
                      </MessageBarBody>
                    </AutoScrollMessageBar>
                    {failures.length > 0 && (
                      <ul className={s.itemList}>
                        {failures.map(item => (
                          <li key={item.index}>
                            <Text weight="semibold">
                              {item.group ? `Group "${item.group}"` : `Submission #${item.index + 1}`}
                            </Text>
                            {' '}({item.sampleCount} sample{item.sampleCount === 1 ? '' : 's'}) — failed
                            {item.errors.length > 0 && (
                              <ul className={s.errors}>
                                {item.errors.map((e, i) => <li key={i}>{e}</li>)}
                              </ul>
                            )}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )
              })()}
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={handleClose}>
              {result ? 'Close' : 'Cancel'}
            </Button>
            <Button
              appearance="primary"
              disabled={!canImport}
              icon={importer.isPending ? <Spinner size="tiny" /> : undefined}
              onClick={onImport}
            >
              {importer.isPending ? 'Importing…' : 'Import'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
