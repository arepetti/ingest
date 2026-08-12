import { useState } from 'react'
import {
  Body1, Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle,
  Dropdown, Field, MessageBarBody, Option, Spinner, Text, makeStyles, tokens,
} from '@fluentui/react-components'
import { AutoScrollMessageBar } from './AutoScrollMessageBar'
import { formatApiError, localizeDiagnostics } from '../api/client'
import { useBulkImport } from '../api/hooks'
import type { Account, BulkImportFormat, BulkImportResult } from '../api/types'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: '12px' },
  fileRow: { display: 'flex', alignItems: 'center', gap: '8px' },
  report: { display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '4px' },
  itemList: { margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '6px' },
  errors: { margin: '2px 0 0', paddingLeft: '18px', color: tokens.colorPaletteRedForeground1 },
  warnings: { margin: '2px 0 0', paddingLeft: '18px', color: tokens.colorPaletteDarkOrangeForeground1 },
  help: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
})

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
  const { t } = useTranslation()
  const importer = useBulkImport()
  const formatLabels: Record<BulkImportFormat, string> = {
    Json: t('schemasSubmissions.bulkImport.formats.json'),
    Csv: t('schemasSubmissions.bulkImport.formats.csv'),
  }

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
          <DialogTitle>{t('schemasSubmissions.bulkImport.title')}</DialogTitle>
          <DialogContent>
            <div className={s.form}>
              <Body1 className={s.help}>
                {t('schemasSubmissions.bulkImport.help')}
              </Body1>

              <Field label={t('schemasSubmissions.common.service')} required>
                <Dropdown
                  placeholder={t('schemasSubmissions.bulkImport.chooseService')}
                  selectedOptions={serviceId ? [serviceId] : []}
                  value={serviceId ? serviceName(serviceId) : ''}
                  onOptionSelect={(_, d) => setServiceId(d.optionValue || '')}
                >
                  {services.map(a => (
                    <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
                  ))}
                </Dropdown>
              </Field>

              <Field label={t('schemasSubmissions.bulkImport.file')} required>
                <div className={s.fileRow}>
                  <input
                    type="file"
                    accept=".json,.csv,application/json,text/csv"
                    onChange={e => onFile(e.target.files?.[0])}
                  />
                </div>
              </Field>

              <Field label={t('schemasSubmissions.bulkImport.format')} hint={t('schemasSubmissions.bulkImport.formatHint')}>
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
                const reports = result.items
                  .map(item => ({
                    item,
                    errors: localizeDiagnostics(item.errorDetails, item.errors),
                    warnings: localizeDiagnostics(item.warningDetails, item.warnings),
                  }))
                  .filter(({ item, warnings }) => (!item.success && !item.skipped) || warnings.length > 0)
                return (
                  <div className={s.report}>
                    <AutoScrollMessageBar intent={result.failed === 0 ? 'success' : 'warning'}>
                      <MessageBarBody>
                        {t('schemasSubmissions.bulkImport.summary', { succeeded: result.succeeded, total: result.total })}
                        {result.skipped > 0 ? ` ${t('schemasSubmissions.bulkImport.skipped', { count: result.skipped })}` : ''}
                        {result.failed > 0 ? ` ${t('schemasSubmissions.bulkImport.failed', { count: result.failed })}` : ''}
                      </MessageBarBody>
                    </AutoScrollMessageBar>
                    {reports.length > 0 && (
                      <ul className={s.itemList}>
                        {reports.map(({ item, errors, warnings }) => (
                          <li key={item.index}>
                            <Text weight="semibold">
                              {item.group
                                ? t('schemasSubmissions.bulkImport.group', { name: item.group })
                                : t('schemasSubmissions.bulkImport.submissionNumber', { number: item.index + 1 })}
                            </Text>
                            {!item.success && <> {' '}{t('schemasSubmissions.bulkImport.itemFailed', { count: item.sampleCount })}</>}
                            {errors.length > 0 && (
                              <ul className={s.errors}>
                                {errors.map((e, i) => <li key={i}>{e}</li>)}
                              </ul>
                            )}
                            {warnings.length > 0 && (
                              <ul className={s.warnings}>
                                {warnings.map((warning, i) => <li key={i}>{warning}</li>)}
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
              {result ? t('schemasSubmissions.common.close') : t('schemasSubmissions.common.cancel')}
            </Button>
            <Button
              appearance="primary"
              disabled={!canImport}
              icon={importer.isPending ? <Spinner size="tiny" /> : undefined}
              onClick={onImport}
            >
              {importer.isPending ? t('schemasSubmissions.bulkImport.importing') : t('schemasSubmissions.bulkImport.import')}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
