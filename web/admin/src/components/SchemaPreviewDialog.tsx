import { useEffect, useMemo, useState } from 'react'
import {
  Badge, Button, Card, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface,
  DialogTitle, Field, Input, MessageBar, MessageBarBody, MessageBarTitle, Text,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { CheckmarkCircle20Regular, Warning20Regular } from '@fluentui/react-icons'
import type { Schema, SchemaValue } from '../api/types'
import { SchemaSampleFields, fromLocalInput, toLocalInput } from './SchemaSampleFields'
import {
  interpretRuleResult, isFilled, safeEval, useSampleRules, type ValueRow,
} from '../utils/sampleRules'

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
  body: { display: 'flex', flexDirection: 'column', minHeight: 0, flex: 1 },
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
})

/** A single problem the preview surfaces. `scope` groups them; `target` is the value label when relevant. */
interface Finding {
  scope: 'required' | 'shape' | 'value' | 'schema'
  target?: string
  message: string
}

/**
 * Interactive, client-side preview of a schema's submission form. Renders the live form from the
 * *unsaved* schema definition, lets the author type values, and reports — best-effort — how the
 * schema's rules would behave: conditional display + inline warnings (via the shared form), plus
 * a results panel covering missing-required, basic shape checks, per-value validation, and
 * schema-level validation. The server remains authoritative; a persistent disclaimer says so.
 */
export function SchemaPreviewDialog({
  schema, open, onClose,
}: {
  schema: Schema
  open: boolean
  onClose: () => void
}) {
  const s = useStyles()

  // Default the sample timestamp to "now". Mirrors the submission editor's single-timestamp model;
  // it also seeds Date-typed inputs that the author leaves blank in their own head.
  const [timestamp, setTimestamp] = useState(() => new Date().toISOString())
  const [rows, setRows] = useState<ValueRow[]>([])

  // Drop values with a blank name (can't be referenced by rules) and keep only the first of any
  // duplicate name (the server would reject a save, but the editor lets you get there transiently).
  const { usableValues, skipped } = useMemo(() => {
    const seen = new Set<string>()
    const usable: SchemaValue[] = []
    const skip: string[] = []
    for (const v of schema.values) {
      const name = (v.name ?? '').trim()
      if (!name) { skip.push(v.label?.trim() || '(unnamed value)'); continue }
      if (seen.has(name)) { skip.push(name); continue }
      seen.add(name)
      usable.push(v)
    }
    return { usableValues: usable, skipped: skip }
  }, [schema])

  // (Re)seed the row model whenever the dialog opens. We intentionally key only on `open` (not on
  // every schema-object identity change) so typed values survive incidental re-renders; the editor
  // isn't interactable while this modal is up, so the schema can't meaningfully change underneath.
  useEffect(() => {
    if (!open) return
    setRows(usableValues.map(v => ({ name: v.name, def: v, value: null, note: '' })))
    setTimestamp(new Date().toISOString())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  // A schema synthesized for the shared form/rule machinery, restricted to the usable values so
  // the layout walker and rule prefetch never trip over blank/duplicate names.
  const previewSchema = useMemo<Schema>(() => ({ ...schema, values: usableValues }), [schema, usableValues])

  const { rowStates, ruleVariables, rulesReady } = useSampleRules(open ? previewSchema : undefined, rows)

  function patchRow(name: string, patch: Partial<ValueRow>) {
    setRows(rs => rs.map(r => r.name === name ? { ...r, ...patch } : r))
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
      if (v.required && v.enabled && !filledNames.has(v.name) && !dropped.has(v.name)) {
        out.push({ scope: 'required', target: v.label || v.name, message: 'required value is empty' })
      }
    }

    // Basic shape checks + per-value validation, for filled, non-dropped rows only (matches the
    // server's "conditional display runs first, then shape, then value rules" ordering).
    for (const r of filled) {
      const def = r.def
      const label = def.label || def.name
      for (const m of shapeProblems(def, r.value)) out.push({ scope: 'shape', target: label, message: m })

      if (def.valueValidation && def.valueValidation.trim()) {
        const verdict = interpretRuleResult(safeEval(def.valueValidation, ruleVariables))
        if (!verdict.ok) {
          out.push({ scope: 'value', target: label, message: verdict.message || 'value validation rule rejected this value' })
        }
      }
    }

    // Schema-level rules — each runs once against the unified context.
    for (const rule of previewSchema.submissionValidations ?? []) {
      if (!rule || !rule.trim()) continue
      const verdict = interpretRuleResult(safeEval(rule, ruleVariables))
      if (!verdict.ok) {
        out.push({ scope: 'schema', message: verdict.message || 'schema-level rule rejected this submission' })
      }
    }

    void statesByName
    return out
  }, [rows, rowStates, ruleVariables, rulesReady, usableValues, previewSchema])

  const hasValues = usableValues.length > 0

  return (
    <Dialog open={open} onOpenChange={(_, d) => { if (!d.open) onClose() }}>
      <DialogSurface className={s.surface}>
        <DialogBody className={s.body}>
          <DialogTitle>Preview: {schema.label || schema.name || 'Untitled schema'}</DialogTitle>
          <DialogContent className={s.content}>
            <MessageBar intent="info">
              <MessageBarBody>
                <MessageBarTitle>Best-effort preview</MessageBarTitle>
                This renders the form and evaluates rules <strong>in your browser</strong> against the
                unsaved schema. The server stays authoritative — some semantics differ here
                (regex dialect, and helpers like <code>sampleTimestamp()</code>/<code>serviceName()</code> aren&apos;t
                available client-side), so always confirm with a real submission or the API before relying on a rule.
              </MessageBarBody>
            </MessageBar>

            {skipped.length > 0 && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  Skipped {skipped.length} value{skipped.length === 1 ? '' : 's'} with a blank or duplicate
                  name: {skipped.join(', ')}. Give every value a unique name to preview it.
                </MessageBarBody>
              </MessageBar>
            )}

            {!hasValues ? (
              <MessageBar intent="warning">
                <MessageBarBody>This schema has no usable values to preview yet. Add a value first.</MessageBarBody>
              </MessageBar>
            ) : (
              <div className={s.grid}>
                <div className={s.formCol}>
                  <Card className={s.formCard}>
                    <Field label="Sample timestamp" className={s.pickerRow}
                      hint="Used for the samples and any date-based rules that read a Date value.">
                      <Input
                        type="datetime-local"
                        value={toLocalInput(timestamp)}
                        onChange={(_, v) => setTimestamp(v.value ? fromLocalInput(v.value) : new Date().toISOString())}
                      />
                    </Field>
                    <SchemaSampleFields
                      schema={previewSchema}
                      rows={rows}
                      rowStates={rowStates}
                      onPatchRow={patchRow}
                    />
                  </Card>
                </div>

                <div className={s.resultsCol}>
                  <Card className={s.resultsCard}>
                    <span className={s.resultsTitle}>Validation results</span>
                    {findings.length === 0 ? (
                      <div className={s.okBanner}>
                        <CheckmarkCircle20Regular />
                        <span>No problems with the current values.</span>
                      </div>
                    ) : (
                      <FindingGroupList findings={findings} styles={s} />
                    )}
                    <Text className={s.muted}>
                      Conditional display (hide/grey) and inline warnings appear on the form to the left.
                    </Text>
                  </Card>
                </div>
              </div>
            )}
          </DialogContent>
          <DialogActions>
            {hasValues && (
              <Button
                appearance="secondary"
                onClick={() => {
                  setRows(usableValues.map(v => ({ name: v.name, def: v, value: null, note: '' })))
                  setTimestamp(new Date().toISOString())
                }}
              >
                Reset values
              </Button>
            )}
            <Button appearance="primary" onClick={onClose}>Close</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

const scopeLabels: Record<Finding['scope'], string> = {
  required: 'Missing required',
  shape: 'Shape checks',
  value: 'Value validation',
  schema: 'Schema-level validation',
}

function FindingGroupList({ findings, styles }: { findings: Finding[]; styles: ReturnType<typeof useStyles> }) {
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

/**
 * Client-side approximation of the server's shape checks for one filled value. Best-effort: regex
 * is evaluated with the JS engine (dialect may differ from .NET), and we only flag what we can be
 * reasonably confident about. Conditional-display and required checks are handled by the caller.
 */
function shapeProblems(def: SchemaValue, value: unknown): string[] {
  const problems: string[] = []
  if (def.type === 'Integer' || def.type === 'Number') {
    const n = typeof value === 'number' ? value : Number(value)
    if (Number.isNaN(n)) {
      problems.push('value is not a number')
    } else {
      if (def.type === 'Integer' && !Number.isInteger(n)) problems.push('value must be a whole number')
      if (def.min != null && n < def.min) problems.push(`below the minimum of ${def.min}`)
      if (def.max != null && n > def.max) problems.push(`above the maximum of ${def.max}`)
    }
  } else if (def.type === 'String') {
    const str = String(value)
    if (def.minLength != null && str.length < def.minLength) problems.push(`shorter than the minimum length of ${def.minLength}`)
    if (def.maxLength != null && str.length > def.maxLength) problems.push(`longer than the maximum length of ${def.maxLength}`)
    if (def.regexPattern && def.regexPattern.trim()) {
      try {
        if (!new RegExp(def.regexPattern).test(str)) problems.push(`does not match the pattern ${def.regexPattern}`)
      } catch {
        problems.push('regex pattern could not be evaluated in the browser (will be checked on the server)')
      }
    }
  } else if (def.type === 'Date') {
    const d = typeof value === 'string' ? new Date(value) : null
    if (!d || Number.isNaN(d.getTime())) {
      problems.push('value is not a valid date')
    } else {
      if (def.minDate) { const min = new Date(def.minDate); if (!Number.isNaN(min.getTime()) && d < min) problems.push(`earlier than ${def.minDate}`) }
      if (def.maxDate) { const max = new Date(def.maxDate); if (!Number.isNaN(max.getTime()) && d > max) problems.push(`later than ${def.maxDate}`) }
    }
  }
  return problems
}
