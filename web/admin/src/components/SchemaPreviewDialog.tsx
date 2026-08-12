import { useMemo, useState } from 'react'
import {
  Badge, Button, Card, Checkbox, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface,
  DialogTitle, Divider, Dropdown, Field, Input, MessageBar, MessageBarBody, MessageBarTitle, Option, Spinner, Text,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { CheckmarkCircle20Regular, Dismiss24Regular, Warning20Regular } from '@fluentui/react-icons'
import type { SampleInput, Schema, SchemaValue, SubmissionValidationResponse } from '../api/types'
import { useAccounts, useValidateSubmissionPreview } from '../api/hooks'
import { formatApiError, localizeDiagnostics } from '../api/client'
import { SchemaSampleFields } from './SchemaSampleFields'
import { fromLocalInput, toLocalInput } from '../utils/datetimeLocal'
import {
  interpretRuleResult, isFilled, safeEval, useSampleRules, type ValueRow,
} from '../utils/sampleRules'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

const useStyles = makeStyles({
  surface: {
    // There is no first-class "full screen" Dialog in Fluent, so we stretch the surface to most
    // of the viewport and let the body scroll. Wide enough for the two-column form/results split.
    maxWidth: '96vw',
    width: '1180px',
    height: '92vh',
    display: 'flex',
    flexDirection: 'column',
  },
  // Keep DialogBody as Fluent's native grid (title / content / actions). We only stretch it to fill
  // the tall surface; overriding `display` here breaks the grid and pushes the title's close action
  // onto its own line.
  body: { minHeight: 0, flex: 1 },
  content: { display: 'flex', flexDirection: 'column', gap: '12px', overflow: 'hidden', flex: 1, minHeight: 0 },
  // Two columns on wide screens (form | results), stacking under a narrow surface.
  grid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 2fr) minmax(300px, 1fr)',
    gap: '16px',
    flex: 1,
    minHeight: 0,
    '@media (max-width: 900px)': { gridTemplateColumns: '1fr' },
  },
  formCol: { overflowY: 'auto', minHeight: 0, paddingRight: '4px' },
  resultsCol: { overflowY: 'auto', minHeight: 0, display: 'flex', flexDirection: 'column', gap: '12px' },
  formCard: { padding: '16px', display: 'flex', flexDirection: 'column', gap: '12px' },
  pickerRow: { maxWidth: '320px' },
  resultsCard: { padding: '14px', display: 'flex', flexDirection: 'column', gap: '10px' },
  resultsTitle: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300 },
  findingList: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: '8px' },
  finding: { display: 'flex', gap: '8px', alignItems: 'flex-start', fontSize: tokens.fontSizeBase200 },
  findingFail: { color: tokens.colorPaletteRedForeground1, flexShrink: 0 },
  findingOk: { color: tokens.colorPaletteGreenForeground1, flexShrink: 0 },
  findingTarget: { fontWeight: tokens.fontWeightSemibold },
  okBanner: { display: 'flex', gap: '8px', alignItems: 'center', color: tokens.colorPaletteGreenForeground1, fontSize: tokens.fontSizeBase200 },
  muted: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  serverResults: { display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '4px' },
  serverList: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: '6px' },
  // The "validate as service" controls embedded at the top in Test-submission mode.
  serverControls: { display: 'flex', gap: '16px', flexWrap: 'wrap', alignItems: 'flex-start' },
  serverControlField: { minWidth: '260px', flex: 1 },
  // Right-align the footer buttons like every other dialog in the app.
  actions: { display: 'flex', justifyContent: 'flex-end', gap: '8px' },
})

/** A single problem the preview surfaces. `scope` groups them; `target` is the value label when relevant. */
interface Finding {
  scope: 'required' | 'shape' | 'value' | 'schema'
  target?: string
  message: string
}

/**
 * Interactive preview of a schema's submission form. Two modes:
 *
 * - `preview` (default) — best-effort, **client-side** preview from the (possibly unsaved) schema:
 *   renders the live form, evaluates rules in the browser, and reports conditional display, inline
 *   warnings, missing-required, shape, per-value and schema-level findings. The server stays
 *   authoritative; a persistent disclaimer says so.
 * - `test` — "Test submission" against a **saved** schema: the same form, plus a service picker and
 *   timestamp at the top, and a primary **Validate** action that runs the real server validation
 *   (cadence, history, approval) without saving. Used from the schema list's row action.
 */
export function SchemaPreviewDialog({
  schema, open, onClose, mode = 'preview',
}: {
  schema: Schema
  open: boolean
  onClose: () => void
  mode?: 'preview' | 'test'
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const serverMode = mode === 'test'

  // Default the sample timestamp to "now". Mirrors the submission editor's single-timestamp model;
  // it also seeds Date-typed inputs that the author leaves blank in their own head.
  const [timestamp, setTimestamp] = useState(() => new Date().toISOString())
  const [rows, setRows] = useState<ValueRow[]>([])

  // Test-submission (server validation) state — only used when serverMode is true. The real check
  // needs a concrete service (visibility/cadence/history/approval) and timestamped samples, so the
  // user supplies the service here; the timestamp is shared with the form below.
  const services = useAccounts({ role: 'Service' }, open && serverMode)
  const validate = useValidateSubmissionPreview()
  const [serviceId, setServiceId] = useState<string | undefined>(undefined)
  const [skipCadence, setSkipCadence] = useState(false)
  const [serverResult, setServerResult] = useState<SubmissionValidationResponse | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)

  // Drop values with a blank name (can't be referenced by rules) and keep only the first of any
  // duplicate name (the server would reject a save, but the editor lets you get there transiently).
  const { usableValues, skipped } = useMemo(() => {
    const seen = new Set<string>()
    const usable: SchemaValue[] = []
    const skip: string[] = []
    for (const v of schema.values) {
      const name = (v.name ?? '').trim()
      if (!name) { skip.push(v.label?.trim() || t('schemasSubmissions.preview.unnamedValue')); continue }
      if (seen.has(name)) { skip.push(name); continue }
      seen.add(name)
      usable.push(v)
    }
    return { usableValues: usable, skipped: skip }
  }, [schema, t])

  // (Re)seed the row model whenever the dialog opens. Done as a render-time reset keyed on the
  // open→true transition (rather than an effect) so typed values survive incidental re-renders; the
  // editor isn't interactable while this modal is up, so the schema can't meaningfully change
  // underneath. See React's "you might not need an effect" reset-on-prop-change pattern.
  const [wasOpen, setWasOpen] = useState(false)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setRows(usableValues.map(v => ({ name: v.name, def: v, value: null, note: '' })))
      setTimestamp(new Date().toISOString())
      setServerResult(null)
      setServerError(null)
    }
  }

  // A schema synthesized for the shared form/rule machinery, restricted to the usable values so
  // the layout walker and rule prefetch never trip over blank/duplicate names.
  const previewSchema = useMemo<Schema>(() => ({ ...schema, values: usableValues }), [schema, usableValues])

  const { rowStates, ruleVariables, rulesReady } = useSampleRules(open ? previewSchema : undefined, rows)

  function patchRow(name: string, patch: Partial<ValueRow>) {
    setRows(rs => rs.map(r => r.name === name ? { ...r, ...patch } : r))
  }

  function resetValues() {
    setRows(usableValues.map(v => ({ name: v.name, def: v, value: null, note: '' })))
    setTimestamp(new Date().toISOString())
    setServerResult(null)
    setServerError(null)
  }

  // Run the real server validation as the chosen service (Test-submission mode only). The endpoint
  // always returns 200 with a verdict; a thrown error here means the request itself failed.
  async function runServer() {
    if (!serviceId) return
    setServerError(null)
    setServerResult(null)
    try {
      const r = await validate.mutateAsync({
        serviceAccountId: serviceId,
        samples: buildServerSamples(timestamp),
        omit: skipCadence ? 'cadence' : undefined,
      })
      setServerResult(r)
    } catch (e) {
      setServerError(formatApiError(e))
    }
  }

  // Build the samples to send to the server, mirroring SubmissionEditPage.buildPayload for a
  // publish: only filled rows that aren't conditionally discarded, stamped with the chosen time.
  function buildServerSamples(ts: string): SampleInput[] {
    const dropped = new Set(rowStates.filter(st => st.discarded).map(st => st.name))
    return rows
      .filter(r => isFilled(r.value) && !dropped.has(r.name) && r.def.kind !== 'Calculated')
      .map(r => ({
        schemaName: previewSchema.name,
        valueName: r.name,
        value: r.value,
        timestamp: ts,
        note: r.note || null,
      }))
  }

  // Everything in the results panel, recomputed as the user types (and again once rule scripts
  // finish translating — hence the `rulesReady` dependency).
  const findings = useMemo<Finding[]>(() => {
    void rulesReady
    const out: Finding[] = []
    const statesByName = new Map(rowStates.map(st => [st.name, st] as const))
    const dropped = new Set(rowStates.filter(st => st.discarded).map(st => st.name))
    const filled = rows.filter(r => isFilled(r.value) && !dropped.has(r.name))
    const filledNames = new Set(filled.map(r => r.name))

    // Missing required — mirrors buildPayload: a required+enabled value that's neither filled nor
    // dropped by a conditional-display rule.
    for (const v of usableValues) {
      if (v.required && v.enabled && v.kind !== 'Calculated' && !filledNames.has(v.name) && !dropped.has(v.name)) {
        out.push({ scope: 'required', target: v.label || v.name, message: t('schemasSubmissions.preview.findings.requiredEmpty') })
      }
    }

    // Basic shape checks + per-value validation, for filled, non-dropped rows only (matches the
    // server's "conditional display runs first, then shape, then value rules" ordering).
    for (const r of filled) {
      const def = r.def
      const label = def.label || def.name
      for (const m of shapeProblems(def, r.value, t)) out.push({ scope: 'shape', target: label, message: m })

      if (def.valueValidation && def.valueValidation.trim()) {
        const verdict = interpretRuleResult(safeEval(def.valueValidation, ruleVariables))
        if (!verdict.ok) {
          out.push({ scope: 'value', target: label, message: verdict.message || t('schemasSubmissions.preview.findings.valueRejected') })
        }
      }
    }

    // Schema-level rules — each runs once against the unified context.
    for (const rule of previewSchema.submissionValidations ?? []) {
      if (!rule || !rule.trim()) continue
      const verdict = interpretRuleResult(safeEval(rule, ruleVariables))
      if (!verdict.ok) {
        out.push({ scope: 'schema', message: verdict.message || t('schemasSubmissions.preview.findings.schemaRejected') })
      }
    }

    void statesByName
    return out
  }, [rows, rowStates, ruleVariables, rulesReady, usableValues, previewSchema, t])

  const hasValues = usableValues.length > 0

  const serviceItems = services.data?.items ?? []
  const selectedService = serviceItems.find(a => a.id === serviceId)
  const sampleCount = buildServerSamples(timestamp).length
  const title = t(serverMode
    ? 'schemasSubmissions.preview.testTitle'
    : 'schemasSubmissions.preview.previewTitle', {
    name: schema.label || schema.name || t('schemasSubmissions.common.untitledSchema'),
  })

  return (
    <Dialog open={open} onOpenChange={(_, d) => { if (!d.open) onClose() }}>
      <DialogSurface className={s.surface}>
        <DialogBody className={s.body}>
          <DialogTitle
            action={<Button appearance="subtle" aria-label={t('schemasSubmissions.common.close')} icon={<Dismiss24Regular />} onClick={onClose} />}
          >
            {title}
          </DialogTitle>
          <DialogContent className={s.content}>
            {serverMode ? (
              <MessageBar intent="info">
                <MessageBarBody>
                  <MessageBarTitle>{t('schemasSubmissions.preview.testHeading')}</MessageBarTitle>
                  {t('schemasSubmissions.preview.testHelp')}
                </MessageBarBody>
              </MessageBar>
            ) : (
              <MessageBar intent="info">
                <MessageBarBody>
                  <MessageBarTitle>{t('schemasSubmissions.preview.bestEffortHeading')}</MessageBarTitle>
                  {t('schemasSubmissions.preview.bestEffortHelp')}
                </MessageBarBody>
              </MessageBar>
            )}

            {skipped.length > 0 && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  {t('schemasSubmissions.preview.skippedValues', { count: skipped.length, names: skipped.join(', ') })}
                </MessageBarBody>
              </MessageBar>
            )}

            {serverMode && hasValues && (
              <Card className={s.formCard}>
                <div className={s.serverControls}>
                  <Field label={t('schemasSubmissions.preview.validateAsService')} required className={s.serverControlField}>
                    <Dropdown
                      placeholder={services.isLoading
                        ? t('schemasSubmissions.preview.loadingServices')
                        : t('schemasSubmissions.preview.selectService')}
                      value={selectedService ? (selectedService.label || selectedService.name) : ''}
                      selectedOptions={serviceId ? [serviceId] : []}
                      onOptionSelect={(_, d) => setServiceId(d.optionValue)}
                    >
                      {serviceItems.map(a => (
                        <Option key={a.id} value={a.id} text={a.label || a.name}>
                          {a.label || a.name}
                        </Option>
                      ))}
                    </Dropdown>
                  </Field>
                  <Field label={t('schemasSubmissions.preview.sampleTimestamp')} className={s.serverControlField}
                    hint={t('schemasSubmissions.preview.timestampServerHint')}>
                    <Input
                      type="datetime-local"
                      value={toLocalInput(timestamp)}
                      onChange={(_, v) => setTimestamp(v.value ? fromLocalInput(v.value) : new Date().toISOString())}
                    />
                  </Field>
                </div>
                <Checkbox
                  label={t('schemasSubmissions.preview.skipCadence')}
                  checked={skipCadence}
                  onChange={(_, d) => setSkipCadence(!!d.checked)}
                />
                <Text className={s.muted}>
                  {sampleCount === 0
                    ? t('schemasSubmissions.preview.noFilledValues')
                    : t('schemasSubmissions.preview.validationCount', { count: sampleCount })}
                </Text>
              </Card>
            )}

            {!hasValues ? (
              <MessageBar intent="warning">
                <MessageBarBody>{t('schemasSubmissions.preview.noUsableValues')}</MessageBarBody>
              </MessageBar>
            ) : (
              <div className={s.grid}>
                <div className={s.formCol}>
                  <Card className={s.formCard}>
                    {!serverMode && (
                      <Field label={t('schemasSubmissions.preview.sampleTimestamp')} className={s.pickerRow}
                        hint={t('schemasSubmissions.preview.timestampPreviewHint')}>
                        <Input
                          type="datetime-local"
                          value={toLocalInput(timestamp)}
                          onChange={(_, v) => setTimestamp(v.value ? fromLocalInput(v.value) : new Date().toISOString())}
                        />
                      </Field>
                    )}
                    <SchemaSampleFields
                      schema={previewSchema}
                      rows={rows}
                      rowStates={rowStates}
                      ruleVariables={ruleVariables}
                      onPatchRow={patchRow}
                    />
                  </Card>
                </div>

                <div className={s.resultsCol}>
                  <Card className={s.resultsCard}>
                    <span className={s.resultsTitle}>{t('schemasSubmissions.preview.validationResults')}</span>
                    {findings.length === 0 ? (
                      <div className={s.okBanner}>
                        <CheckmarkCircle20Regular />
                        <span>{t('schemasSubmissions.preview.noProblems')}</span>
                      </div>
                    ) : (
                      <FindingGroupList findings={findings} styles={s} />
                    )}
                    <Text className={s.muted}>
                      {t('schemasSubmissions.preview.conditionalHelp')}
                    </Text>

                    {serverMode && (serverError || serverResult) && (
                      <>
                        <Divider />
                        <span className={s.resultsTitle}>{t('schemasSubmissions.preview.serverValidation')}</span>
                        {serverError && (
                          <MessageBar intent="error">
                            <MessageBarBody style={{ whiteSpace: 'pre-line' }}>{serverError}</MessageBarBody>
                          </MessageBar>
                        )}
                        {serverResult && <ServerVerdict result={serverResult} styles={s} />}
                      </>
                    )}
                  </Card>
                </div>
              </div>
            )}
          </DialogContent>
          <DialogActions className={s.actions}>
            {hasValues && (
              <Button appearance="secondary" onClick={resetValues}>{t('schemasSubmissions.preview.resetValues')}</Button>
            )}
            <Button appearance="secondary" onClick={onClose}>{t('schemasSubmissions.common.close')}</Button>
            {serverMode && hasValues && (
              <Button
                appearance="primary"
                icon={validate.isPending ? <Spinner size="tiny" /> : undefined}
                disabled={!serviceId || validate.isPending}
                onClick={runServer}
              >
                {t('schemasSubmissions.preview.validate')}
              </Button>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

function FindingGroupList({ findings, styles }: { findings: Finding[]; styles: ReturnType<typeof useStyles> }) {
  const { t } = useTranslation()
  const scopeLabels: Record<Finding['scope'], string> = {
    required: t('schemasSubmissions.preview.scopes.required'),
    shape: t('schemasSubmissions.preview.scopes.shape'),
    value: t('schemasSubmissions.preview.scopes.value'),
    schema: t('schemasSubmissions.preview.scopes.schema'),
  }
  // Group by scope so the panel reads "Missing required → Shape → Value → Schema", matching the
  // order the server evaluates them in.
  const order: Finding['scope'][] = ['required', 'shape', 'value', 'schema']
  return (
    <>
      {order.map(scope => {
        const items = findings.filter(f => f.scope === scope)
        if (items.length === 0) return null
        return (
          <div key={scope} style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <Badge appearance="tint" color="danger" size="small">{scopeLabels[scope]}</Badge>
            <ul className={styles.findingList}>
              {items.map((f, i) => (
                <li key={i} className={styles.finding}>
                  <Warning20Regular className={styles.findingFail} />
                  <span>
                    {f.target && <span className={styles.findingTarget}>{f.target}: </span>}
                    {f.message}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )
      })}
    </>
  )
}

/** Render the server's dry-run verdict: validity headline, would-be approval, then errors/warnings/discards. */
function ServerVerdict({ result, styles }: { result: SubmissionValidationResponse; styles: ReturnType<typeof useStyles> }) {
  const { t } = useTranslation()
  const errors = localizeDiagnostics(result.errorDetails, result.errors)
  const warnings = localizeDiagnostics(result.warningDetails, result.warnings)
  const approvalNote = result.approvalStatus === 'Pending'
    ? t('schemasSubmissions.preview.server.heldForApproval', { count: result.requiredApprovers.length })
    : t('schemasSubmissions.preview.server.acceptedImmediately')

  return (
    <div className={styles.serverResults}>
      {result.valid ? (
        <div className={styles.okBanner}>
          <CheckmarkCircle20Regular />
          <span>{t('schemasSubmissions.preview.server.valid')}</span>
        </div>
      ) : (
        <Badge appearance="tint" color="danger">{t('schemasSubmissions.preview.server.invalid', { count: errors.length })}</Badge>
      )}

      {errors.length > 0 && (
        <ul className={styles.serverList}>
          {errors.map((e, i) => (
            <li key={i} className={styles.finding}>
              <Warning20Regular className={styles.findingFail} />
              <span>{e}</span>
            </li>
          ))}
        </ul>
      )}

      {warnings.length > 0 && (
        <>
          <Badge appearance="tint" color="warning" size="small">{t('schemasSubmissions.common.warnings')}</Badge>
          <ul className={styles.serverList}>
            {warnings.map((w, i) => (
              <li key={i} className={styles.finding}>
                <Warning20Regular className={styles.findingOk} />
                <span>{w}</span>
              </li>
            ))}
          </ul>
        </>
      )}

      {result.discardedSamples.length > 0 && (
        <Text className={styles.muted}>
          {t('schemasSubmissions.preview.server.discarded', {
            names: result.discardedSamples.map(d => d.valueName).join(', '),
          })}
        </Text>
      )}

      {result.valid && <Text className={styles.muted}>{approvalNote}</Text>}
    </div>
  )
}

/**
 * Client-side approximation of the server's shape checks for one filled value. Best-effort: regex
 * is evaluated with the JS engine (dialect may differ from .NET), and we only flag what we can be
 * reasonably confident about. Conditional-display and required checks are handled by the caller.
 */
function shapeProblems(def: SchemaValue, value: unknown, t: TFunction): string[] {
  const problems: string[] = []
  if (def.type === 'Integer' || def.type === 'Number') {
    const n = typeof value === 'number' ? value : Number(value)
    if (Number.isNaN(n)) {
      problems.push(t('schemasSubmissions.preview.shape.notNumber'))
    } else {
      if (def.type === 'Integer' && !Number.isInteger(n)) problems.push(t('schemasSubmissions.preview.shape.wholeNumber'))
      if (def.min != null && n < def.min) problems.push(t('schemasSubmissions.preview.shape.belowMinimum', { value: def.min }))
      if (def.max != null && n > def.max) problems.push(t('schemasSubmissions.preview.shape.aboveMaximum', { value: def.max }))
    }
  } else if (def.type === 'String') {
    const str = String(value)
    if (def.minLength != null && str.length < def.minLength) problems.push(t('schemasSubmissions.preview.shape.shorter', { value: def.minLength }))
    if (def.maxLength != null && str.length > def.maxLength) problems.push(t('schemasSubmissions.preview.shape.longer', { value: def.maxLength }))
    if (def.regexPattern && def.regexPattern.trim()) {
      try {
        if (!new RegExp(def.regexPattern).test(str)) problems.push(t('schemasSubmissions.preview.shape.pattern', { value: def.regexPattern }))
      } catch {
        problems.push(t('schemasSubmissions.preview.shape.regexBrowser'))
      }
    }
  } else if (def.type === 'Date') {
    const d = typeof value === 'string' ? new Date(value) : null
    if (!d || Number.isNaN(d.getTime())) {
      problems.push(t('schemasSubmissions.preview.shape.invalidDate'))
    } else {
      if (def.minDate) { const min = new Date(def.minDate); if (!Number.isNaN(min.getTime()) && d < min) problems.push(t('schemasSubmissions.preview.shape.earlier', { value: def.minDate })) }
      if (def.maxDate) { const max = new Date(def.maxDate); if (!Number.isNaN(max.getTime()) && d > max) problems.push(t('schemasSubmissions.preview.shape.later', { value: def.maxDate })) }
    }
  }
  return problems
}
