import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  Badge, Button, Card, Dropdown, Field, Input,
  MessageBar, MessageBarBody, MessageBarTitle,
  Option, Textarea, Title2, Toolbar, ToolbarButton,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowLeft20Regular } from '@fluentui/react-icons'
import {
  useAccounts, useAdminCreateSubmission, useAdminUpdateSubmission,
  useCreateMySubmission, useMe, useMySchemas, useMySubmission,
  useReplaceMySubmission, useSchemas, useSubmission,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import type {
  AdminSubmissionInput, SampleInput, Schema, SchemaValue, SchemaValueType,
} from '../api/types'
import { AccountAvatar, SchemaAvatar } from '../components/Avatars'
import { ValueLabel } from '../components/ValueLabel'
import {
  ExpressionError, isTruthy, prefetchExpressions, tryEvaluateExpression,
} from '../utils/expression'
import { walkLayout } from '../utils/layout'
import { cadenceLabel } from '../utils/cadence'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  // `alignItems: start` keeps every Field anchored to the top of its grid cell. Without it,
  // the row's height is driven by the tallest Field (the Timestamp, which has a hint), and the
  // shorter Fields' controls drift downwards as Fluent centers them in the stretched cell.
  pickers: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '12px', padding: '16px', alignItems: 'start' },
  valuesCard: { padding: '16px', display: 'flex', flexDirection: 'column', gap: '16px' },
  valuesHeader: { display: 'flex', alignItems: 'center', gap: '12px' },
  valueRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1.6fr) minmax(0, 1.4fr) minmax(0, 1fr)',
    gap: '12px',
    alignItems: 'flex-start',
    padding: '12px 0',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  valueRowFirst: { borderTop: 'none' },
  // Section headings rendered as `<h2>` / `<h3>` per nesting depth. Tone is a bit lighter than
  // the per-value Caption so the hierarchy "Section > Caption > Field" reads top to bottom.
  sectionHeading: {
    margin: '20px 0 0',
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  subsectionHeading: {
    margin: '14px 0 0',
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  sectionDescription: {
    margin: '2px 0 6px',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  // Schema-author-provided heading rendered above a value's row (think <h2>). Display-only.
  valueCaption: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase500,
    color: tokens.colorNeutralForeground1,
    margin: '16px 0 4px',
  },
  valueLabel: { display: 'flex', flexDirection: 'column', gap: '4px' },
  valueLabelTitle: { fontWeight: 600 },
  valueLabelMeta: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  badges: { display: 'flex', gap: '6px', flexWrap: 'wrap', marginTop: '4px' },
  // When the textarea is hidden, the "Add notes" button sits in the same column the textarea
  // would occupy. The top padding lines it up with the value input (i.e. below the field labels).
  notesPlaceholder: { paddingTop: '24px' },
  // Live warning text shown under the value label when a per-value Warning rule fires.
  warningInline: {
    color: tokens.colorPaletteDarkOrangeForeground1,
    fontSize: '12px',
    fontStyle: 'italic',
  },
})

interface ValueRow {
  /** value name (unique within the schema) */
  name: string
  def: SchemaValue
  /** Raw value as the user typed it; we serialize it on save. */
  value: unknown
  note: string
}

/** Outcome of client-side EnabledIf/VisibleIf/Warning evaluation for a single row. */
interface RowState {
  name: string
  /** True when VisibleIf is set and evaluates falsy — the row is not rendered at all. */
  hidden: boolean
  /** True when EnabledIf is set and evaluates falsy — the row is rendered read-only. */
  disabled: boolean
  /** True when this row is effectively dropped from the payload (hidden OR disabled). */
  discarded: boolean
  /** Optional live warning text from the per-value Warning rule. */
  warning: string | null
}

/**
 * Same page, three personalities:
 *  - `new`   → blank form, pickers active, Save submits a new submission.
 *  - `edit`  → hydrate from existing submission, pickers locked, Save replaces it.
 *  - `view`  → hydrate from existing submission, every input disabled, Save hidden. The
 *              user gets the exact same layout (sections, captions, units, warnings) they
 *              see while editing, just without the ability to change anything.
 */
export interface SubmissionEditPageProps {
  /** When true, the page renders read-only. The submission id still comes from the URL. */
  readOnly?: boolean
}

export function SubmissionEditPage({ readOnly = false }: SubmissionEditPageProps = {}) {
  const s = useStyles()
  const nav = useNavigate()
  const { id } = useParams<{ id?: string }>()
  // In view mode we always have an id and treat the page like an edit (so the same hydration
  // path runs); the read-only flag then suppresses every mutation surface.
  const isEdit = !!id

  const { data: me } = useMe()
  const isService = me?.role === 'Service'

  // Different data sources depending on role: service callers can only see their own visible schemas
  // and use /api/submissions; everyone else uses the admin listings.
  const services    = useAccounts({ role: 'Service' }, !isService)
  const adminSchemas = useSchemas(undefined, !isService)
  const myVisible   = useMySchemas(isService)

  const adminExisting   = useSubmission(id, !isService && isEdit)
  const myExisting      = useMySubmission(id, isService && isEdit)
  const existing        = isService ? myExisting : adminExisting

  const adminCreate = useAdminCreateSubmission()
  const adminUpdate = useAdminUpdateSubmission()
  const myCreate    = useCreateMySubmission()
  const myUpdate    = useReplaceMySubmission()

  const [serviceId, setServiceId] = useState<string | undefined>(undefined)
  const [schemaName, setSchemaName] = useState<string>('')
  const [timestamp, setTimestamp] = useState<string>(() => new Date().toISOString())
  const [rows, setRows] = useState<ValueRow[]>([])
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [missingRequired, setMissingRequired] = useState<string[]>([])
  const [serverWarnings, setServerWarnings] = useState<string[]>([])
  const [prefilled, setPrefilled] = useState(false)
  // Bumped after the translator has fetched all rule scripts for the current schema. Used
  // purely as a re-render trigger so `evaluateGating` (which reads from a module-level
  // cache) re-runs and starts producing concrete results.
  const [rulesReady, setRulesReady] = useState(0)

  // For services the service is always "me" — pin it.
  useEffect(() => {
    if (isService && me?.id && !serviceId) setServiceId(me.id)
  }, [isService, me?.id, serviceId])

  // Source of truth for "which schemas are pickable" depends on the role.
  const schemas: Schema[] = useMemo(() => {
    if (isService) return myVisible.data ?? []
    const all = adminSchemas.data?.items ?? []
    if (!serviceId) return []
    return all.filter(sc => sc.isGlobal || sc.serviceIds.includes(serviceId))
  }, [isService, myVisible.data, adminSchemas.data, serviceId])

  const schema = useMemo(
    () => schemas.find(sc => sc.name === schemaName),
    [schemas, schemaName],
  )

  // When the schema changes, build a fresh row per declared value (preserving anything already in `rows`
  // so users don't lose work if they bounce between schemas while editing).
  useEffect(() => {
    if (!schema) { setRows([]); return }
    setRows(prev => {
      const byName = new Map(prev.map(r => [r.name, r] as const))
      return schema.values.map(v => byName.get(v.name) ?? { name: v.name, def: v, value: null, note: '' })
    })
    setMissingRequired([])
  }, [schema])

  // Ask the server to translate every per-value rule on this schema into JavaScript. The
  // helper caches results so subsequent visits to the same schema don't hit the network.
  // The component re-renders once translations have settled (rulesReady tick), at which
  // point evaluateGating switches from "no opinion" to actual hide/disable/warning verdicts.
  useEffect(() => {
    if (!schema) return
    const exprs: (string | null | undefined)[] = []
    for (const v of schema.values) {
      exprs.push(v.enabledIf, v.visibleIf, v.warning)
    }
    let cancelled = false
    prefetchExpressions(exprs).then(() => {
      if (!cancelled) setRulesReady(t => t + 1)
    })
    return () => { cancelled = true }
  }, [schema])

  // On edit, hydrate state from the existing submission once it has loaded.
  useEffect(() => {
    if (!isEdit || prefilled) return
    const data = existing.data
    if (!data) return

    setServiceId(data.serviceAccountId)
    // The current model allows a submission to mix multiple schemas; for editing we lock to the one
    // most of its samples belong to (with a soft warning shown in the UI for the multi-schema case).
    const firstSchema = data.samples[0]?.schemaName
    if (firstSchema) setSchemaName(firstSchema)
    if (data.samples[0]?.timestamp) setTimestamp(data.samples[0].timestamp)
    setPrefilled(true)
  }, [isEdit, prefilled, existing.data])

  // Once we've prefilled and the schema is resolved, fill the row values from the existing samples.
  useEffect(() => {
    if (!isEdit || !prefilled || !schema || !existing.data) return
    const samplesByValue = new Map(
      existing.data.samples
        .filter(s => s.schemaName === schema.name)
        .map(s => [s.valueName, s] as const),
    )
    setRows(prev => prev.map(r => {
      const ex = samplesByValue.get(r.name)
      if (!ex) return r
      return { ...r, value: ex.value, note: ex.note ?? '' }
    }))
  // We deliberately key on schema.name (not the object identity) so swapping schemas re-runs this.
  }, [isEdit, prefilled, schema?.name, existing.data])

  function patchRow(name: string, patch: Partial<ValueRow>) {
    setRows(rs => rs.map(r => r.name === name ? { ...r, ...patch } : r))
  }

  // Build the variable context used for client-side EnabledIf/VisibleIf/Warning evaluation.
  // Mirrors the server's `BuildRuleContext`: every declared value is exposed by name (null when
  // the row is empty), and each numeric value with bounds also contributes `<name>.minimum` and
  // `<name>.maximum` keys. The dotted keys are unreachable as plain identifiers in NCalc — the
  // server registers them under the bracket form `[name.minimum]`, which `helpers.var` looks up
  // case-insensitively in this bag.
  const ruleVariables = useMemo(() => {
    const ctx: Record<string, unknown> = {}
    for (const r of rows) {
      ctx[r.name] = isFilled(r.value) ? r.value : null
      const isNumeric = r.def.type === 'Integer' || r.def.type === 'Number'
      if (isNumeric) {
        if (r.def.min !== null && r.def.min !== undefined) ctx[`${r.name}.minimum`] = r.def.min
        if (r.def.max !== null && r.def.max !== undefined) ctx[`${r.name}.maximum`] = r.def.max
      }
    }
    return ctx
  }, [rows])

  const rowStates = useMemo(() => {
    // `rulesReady` is read here so the memo invalidates once new translations land in the
    // cache. The cache lookup itself happens inside evaluateGating.
    void rulesReady
    return rows.map(r => evaluateGating(r, ruleVariables))
  }, [rows, ruleVariables, rulesReady])

  function buildPayload(): SampleInput[] | null {
    if (!schema) return null
    // Only include rows the user actually filled — booleans use a tri-state dropdown so "unset" is a
    // first-class option there too. Required-but-empty values are reported back to the user.
    // Rows hidden/disabled by EnabledIf/VisibleIf are silently dropped here (the server would do
    // the same and emit a warning).
    const dropped = new Set(rowStates.filter(s => s.discarded).map(s => s.name))
    const filled = rows.filter(r => isFilled(r.value) && !dropped.has(r.name))
    const filledNames = new Set(filled.map(r => r.name))
    const missing = schema.values
      .filter(v => v.required && v.enabled && !filledNames.has(v.name) && !dropped.has(v.name))
      .map(v => v.label || v.name)
    if (missing.length > 0) {
      setMissingRequired(missing)
      return null
    }
    setMissingRequired([])
    return filled.map(r => ({
      schemaName: schema.name,
      valueName: r.name,
      value: r.value,
      timestamp,
      note: r.note || null,
    }))
  }

  async function onSave() {
    setSubmitError(null)
    setServerWarnings([])
    if (!serviceId) { setSubmitError('Pick a service first.'); return }
    if (!schema)    { setSubmitError('Pick a schema first.'); return }

    const samples = buildPayload()
    if (samples === null) return

    try {
      // If the server reports warnings we surface them and stay on the page so the user can
      // see what happened before navigating away. Otherwise navigate to the detail view.
      const targetId = isEdit && id ? id : undefined
      let warnings: string[] = []
      let createdId: string | undefined

      if (isService) {
        if (targetId) {
          const r = await myUpdate.mutateAsync({ id: targetId, req: { samples } })
          warnings = r.warnings ?? []
        } else {
          const r = await myCreate.mutateAsync({ samples })
          warnings = r.warnings ?? []
          createdId = r.id
        }
      } else {
        const payload: AdminSubmissionInput = { serviceAccountId: serviceId, samples }
        if (targetId) {
          const r = await adminUpdate.mutateAsync({ id: targetId, req: payload })
          warnings = r.warnings ?? []
        } else {
          const r = await adminCreate.mutateAsync(payload)
          warnings = r.warnings ?? []
          createdId = r.id
        }
      }

      if (warnings.length > 0) {
        setServerWarnings(warnings)
        return
      }
      // Land the user on the read-only form view — same layout they were just editing, so
      // their eye stays anchored to the values they entered. The raw-table "view details"
      // page is still reachable from the drawer / row actions on the listing.
      nav(`/submissions/${targetId ?? createdId}/view`)
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  const isBusy = adminCreate.isPending || adminUpdate.isPending || myCreate.isPending || myUpdate.isPending
  const selectedService = !isService ? services.data?.items.find(a => a.id === serviceId) : undefined
  // Best-effort detection: the existing submission used more than one schema.
  const multiSchema = isEdit && (existing.data?.samples ?? []).some(s => s.schemaName !== schemaName) && rows.length > 0

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={() => nav(-1)}>Back</Button>
          <Title2>{readOnly ? 'View submission' : isEdit ? 'Edit submission' : 'New submission'}</Title2>
        </div>
        {!readOnly && (
          <Toolbar>
            <ToolbarButton appearance="primary" disabled={isBusy} onClick={onSave}>
              {isEdit ? 'Save changes' : 'Submit'}
            </ToolbarButton>
          </Toolbar>
        )}
      </div>

      {existing.isLoading && isEdit && <div>Loading...</div>}
      {existing.error && isEdit && (
        <AutoScrollMessageBar intent="error"><MessageBarBody>{formatApiError(existing.error)}</MessageBarBody></AutoScrollMessageBar>
      )}

      <Card>
        <div className={s.pickers}>
          {!isService && (
            <Field label="Service" required>
              <Dropdown
                placeholder="Pick a service"
                disabled={isEdit || readOnly}
                selectedOptions={serviceId ? [serviceId] : []}
                value={selectedService ? (selectedService.label || selectedService.name) : ''}
                onOptionSelect={(_, d) => { setServiceId(d.optionValue); setSchemaName('') }}
              >
                {(services.data?.items ?? []).map(a => (
                  <Option key={a.id} value={a.id} text={a.label || a.name}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <AccountAvatar account={a} size={24} />
                      <span>{a.label || a.name}</span>
                    </div>
                  </Option>
                ))}
              </Dropdown>
            </Field>
          )}
          <Field label="Schema" required>
            <Dropdown
              placeholder={serviceId || isService ? 'Pick a schema' : 'Pick a service first'}
              disabled={isEdit || readOnly || (!isService && !serviceId)}
              selectedOptions={schemaName ? [schemaName] : []}
              value={schema ? (schema.label || schema.name) : ''}
              onOptionSelect={(_, d) => setSchemaName(d.optionValue ?? '')}
            >
              {schemas.map(sc => (
                <Option key={sc.id} value={sc.name} text={sc.label || sc.name}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <SchemaAvatar schema={sc} size={24} />
                    <span>{sc.label || sc.name}</span>
                  </div>
                </Option>
              ))}
            </Dropdown>
          </Field>
          <Field label="Timestamp (UTC)" required hint="Applied to every sample in this submission.">
            <Input
              type="datetime-local"
              disabled={readOnly}
              value={toLocalInput(timestamp)}
              onChange={(_, v) => setTimestamp(fromLocalInput(v.value))}
            />
          </Field>
        </div>
      </Card>

      {!schema && (
        <MessageBar intent="info">
          <MessageBarBody>
            {isService
              ? 'Pick a schema to see the values you can submit.'
              : 'Pick a service and a schema to start entering values.'}
          </MessageBarBody>
        </MessageBar>
      )}

      {multiSchema && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>This submission contained values from multiple schemas.</MessageBarTitle>
            Saving will keep only the values for <strong>{schema?.label || schema?.name}</strong>. Pick another
            schema first if you need to edit those instead.
          </MessageBarBody>
        </MessageBar>
      )}

      {schema && (
        <Card className={s.valuesCard}>
          <div className={s.valuesHeader}>
            <SchemaAvatar schema={schema} />
            <div>
              <div style={{ fontWeight: 600 }}>{schema.label || schema.name}</div>
              <div style={{ color: tokens.colorNeutralForeground3, fontSize: 12 }}>
                {schema.values.length} value(s) · leave optional ones empty to skip them
              </div>
            </div>
          </div>

          {schema.values.length === 0 && (
            <MessageBar intent="warning">
              <MessageBarBody>This schema has no values defined.</MessageBarBody>
            </MessageBar>
          )}

          {(() => {
            // Index rows/states by value name so the layout walker can look them up.
            const rowsByName  = new Map(rows.map(r => [r.name, r] as const))
            const statesByName = new Map(rowStates.map(st => [st.name, st] as const))

            // walkLayout drives the order + grouping. The predicate hides values whose
            // VisibleIf rule evaluates falsy, and the walker folds away sections whose every
            // descendant is hidden — no "Optional notes" heading sitting above nothing.
            const items = walkLayout(schema, {
              isValueVisible: (name) => !(statesByName.get(name)?.hidden ?? false),
            })

            // Track the first visible value globally so its top border is suppressed; a fresh
            // section start also suppresses the border on the first child for the same reason.
            let visibleSoFar = 0
            let suppressNextBorder = false

            return items.map((item, idx) => {
              if (item.kind === 'section-end') return null
              if (item.kind === 'section-start') {
                suppressNextBorder = true
                const HeadingTag = item.depth === 0 ? 'h2' : 'h3'
                const headingClass = item.depth === 0 ? s.sectionHeading : s.subsectionHeading
                return (
                  <div key={`section-${idx}`}>
                    <HeadingTag className={headingClass} style={{ paddingLeft: `${item.depth * 8}px` }}>
                      {item.caption}
                    </HeadingTag>
                    {item.description && (
                      <p className={s.sectionDescription} style={{ paddingLeft: `${item.depth * 8}px` }}>
                        {item.description}
                      </p>
                    )}
                  </div>
                )
              }
              const row = rowsByName.get(item.value.name)
              const state = statesByName.get(item.value.name)
              if (!row || !state) return null
              const caption = item.value.caption?.trim() || ''
              const isFirstVisible = visibleSoFar === 0
              const borderless = isFirstVisible || !!caption || suppressNextBorder
              suppressNextBorder = false
              visibleSoFar++
              return (
                <div key={item.value.name} style={item.depth > 0 ? { paddingLeft: `${item.depth * 8}px` } : undefined}>
                  {caption && (
                    <h2 className={s.valueCaption}>{caption}</h2>
                  )}
                  <SchemaValueRow
                    row={row}
                    first={borderless}
                    schemaEnabled={schema.enabled}
                    schema={schema}
                    state={state}
                    readOnly={readOnly}
                    onChange={patch => patchRow(row.name, patch)}
                  />
                </div>
              )
            })
          })()}
        </Card>
      )}

      {missingRequired.length > 0 && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Missing required values</MessageBarTitle>
            {missingRequired.join(', ')}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {serverWarnings.length > 0 && (
        <AutoScrollMessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Submission accepted with warnings</MessageBarTitle>
            <ul style={{ margin: '4px 0 0 16px', padding: 0 }}>
              {serverWarnings.map((w, i) => <li key={i}>{w}</li>)}
            </ul>
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {submitError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not save submission</MessageBarTitle>
            {submitError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}
    </div>
  )
}

function SchemaValueRow({
  row, first, schemaEnabled, schema, state, readOnly, onChange,
}: {
  row: ValueRow
  first: boolean
  schemaEnabled: boolean
  /** Parent schema — needed by `ValueLabel` for the version-bump anchor + cadence. */
  schema: Schema
  state: RowState
  /** View mode: every input disabled, "Add notes" button suppressed when there's no existing note. */
  readOnly?: boolean
  onChange: (patch: Partial<ValueRow>) => void
}) {
  const s = useStyles()
  const def = row.def
  // The row is "inert" (not editable) when the page is read-only, the schema/value is disabled,
  // or an EnabledIf rule says so. We still render the row in the latter case so the user sees
  // why it's locked.
  const inert = !!readOnly || !schemaEnabled || !def.enabled || state.disabled
  // Keep the row compact by default. The textarea pops out either when the user opts in,
  // or when the row already carries a note (e.g. while editing an existing submission).
  const [notesOpened, setNotesOpened] = useState(false)
  const showNotes = notesOpened || !!row.note

  return (
    <div className={first ? `${s.valueRow} ${s.valueRowFirst}` : s.valueRow}>
      <div className={s.valueLabel}>
        <span className={s.valueLabelTitle}>
          <ValueLabel value={def} schema={schema} descriptionMode="none" showRequired />
        </span>
        {def.description && <span className={s.valueLabelMeta}>{def.description}</span>}
        <div className={s.badges}>
          {/* Same visual treatment as the cadence badge — both are neutral metadata pills. */}
          <Badge appearance="outline" color="informative" size="small">{friendlyTypeLabel(def.type)}</Badge>
          <Badge appearance="outline" color="informative" size="small">{cadenceLabel(def.cadence)}</Badge>
          {def.required && <Badge appearance="outline" color="severe" size="small">required</Badge>}
          {!def.required && <Badge appearance="outline" color="subtle" size="small">optional</Badge>}
          {inert && <Badge appearance="outline" color="subtle" size="small">disabled</Badge>}
        </div>
        <span className={s.valueLabelMeta}>{valueHint(def)}</span>
        {state.warning && (
          <span className={s.warningInline}>{state.warning}</span>
        )}
      </div>

      <Field label={`Value${def.unit ? ` (${def.unit})` : ''}`}>
        <SampleValueInput
          valueDef={def}
          value={row.value}
          onChange={v => onChange({ value: v })}
          disabled={inert}
        />
      </Field>

      {showNotes ? (
        <Field label="Note">
          <Textarea
            value={row.note}
            onChange={(_, v) => onChange({ note: v.value })}
            disabled={inert}
            rows={2}
          />
        </Field>
      ) : readOnly ? (
        // No note on this sample and we're in view mode — leave the column blank rather than
        // showing a perpetually-disabled "Add notes" button.
        <div />
      ) : (
        <div className={s.notesPlaceholder}>
          <Button
            appearance="transparent"
            icon={<Add20Regular />}
            disabled={inert}
            onClick={() => setNotesOpened(true)}
          >
            Add notes
          </Button>
        </div>
      )}
    </div>
  )
}

function SampleValueInput({
  valueDef, value, onChange, disabled,
}: {
  valueDef: SchemaValue
  value: unknown
  onChange: (v: unknown) => void
  disabled?: boolean
}) {
  switch (valueDef.type as SchemaValueType) {
    case 'Boolean':
      // Tri-state: "(empty)" means "not provided" — distinct from explicit false. We surface
      // the choices as "Yes" / "No" to operators because non-technical users don't think in
      // booleans; the underlying option values stay 'true'/'false' so the wire shape doesn't
      // change.
      return (
        <Dropdown
          disabled={disabled}
          selectedOptions={value === true ? ['true'] : value === false ? ['false'] : ['']}
          value={value === true ? 'Yes' : value === false ? 'No' : ''}
          onOptionSelect={(_, d) => {
            const v = d.optionValue
            onChange(v === 'true' ? true : v === 'false' ? false : null)
          }}
        >
          <Option value="">(not provided)</Option>
          <Option value="true">Yes</Option>
          <Option value="false">No</Option>
        </Dropdown>
      )
    case 'Integer':
      return (
        <Input
          disabled={disabled}
          type="number"
          value={value == null ? '' : String(value)}
          onChange={(_, v) => onChange(v.value === '' ? null : Math.trunc(Number(v.value)))}
        />
      )
    case 'Number':
      return (
        <Input
          disabled={disabled}
          type="number"
          value={value == null ? '' : String(value)}
          onChange={(_, v) => onChange(v.value === '' ? null : Number(v.value))}
        />
      )
    case 'Date':
      return (
        <Input
          disabled={disabled}
          type="datetime-local"
          value={typeof value === 'string' ? toLocalInput(value) : ''}
          onChange={(_, v) => onChange(v.value ? fromLocalInput(v.value) : null)}
        />
      )
    case 'String':
    default: {
      // Promote to a multi-line Textarea when the schema author hints that the value can be
      // long (MaxLength > 40 characters). Single-line Input stays the default — most string
      // values are short identifiers, codes, or names that don't benefit from the extra space.
      const multiline = valueDef.maxLength != null && valueDef.maxLength > 40
      const common = {
        disabled,
        value: value == null ? '' : String(value),
        onChange: (_: unknown, v: { value: string }) =>
          onChange(v.value === '' ? null : v.value),
      }
      return multiline
        ? <Textarea {...common} rows={3} />
        : <Input {...common} />
    }
  }
}

function isFilled(v: unknown): boolean {
  if (v === null || v === undefined) return false
  if (typeof v === 'string') return v.trim() !== ''
  // false/0 are valid filled values for booleans and numbers
  return true
}

/**
 * Map a wire-level `SchemaValueType` to the friendlier wording shown to operators in the
 * submission editor. The schema editor still shows the raw type — that audience cares about
 * the precise wire shape, this audience doesn't.
 */
function friendlyTypeLabel(type: SchemaValueType): string {
  switch (type) {
    case 'String':  return 'Text'
    case 'Integer': return 'Whole number'
    case 'Number':  return 'Number'
    case 'Date':    return 'Date'
    case 'Boolean': return 'Yes/No'
  }
}

function valueHint(v: SchemaValue): string {
  const bits: string[] = []
  if (v.type === 'Number' || v.type === 'Integer') {
    if (v.min != null) bits.push(`min ${v.min}`)
    if (v.max != null) bits.push(`max ${v.max}`)
  }
  if (v.type === 'String') {
    if (v.minLength != null) bits.push(`min length ${v.minLength}`)
    if (v.maxLength != null) bits.push(`max length ${v.maxLength}`)
    if (v.regexPattern) bits.push(`regex: ${v.regexPattern}`)
  }
  if (v.type === 'Date') {
    if (v.minDate) bits.push(`from ${v.minDate}`)
    if (v.maxDate) bits.push(`to ${v.maxDate}`)
  }
  return bits.join(' · ')
}

// <input type="datetime-local"> wants 'YYYY-MM-DDTHH:mm' in local time; we round-trip via UTC ISO.
function toLocalInput(iso: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
function fromLocalInput(local: string): string {
  if (!local) return ''
  const d = new Date(local)
  return d.toISOString()
}

/**
 * Evaluate the three rule fields against the live submission context. Returns a
 * `RowState` describing whether the row should be hidden, disabled, or carrying a warning.
 * When a rule fails to parse/evaluate we fall back to "show + no warning" — the server
 * remains authoritative so a buggy rule never makes data silently disappear from the editor.
 */
function evaluateGating(row: ValueRow, variables: Record<string, unknown>): RowState {
  // The unified `variables` bag already carries every value by its name (including this row's
  // own current input) plus the `<name>.minimum` / `<name>.maximum` bound keys. No alias
  // injection needed — rules reference values explicitly. tryEvaluateExpression returns
  // undefined when translation hasn't landed yet; we treat that the same as "no rule" so the
  // UI stays permissive until the verdict is in.
  const safeEval = (expr: string | null | undefined): unknown | undefined => {
    if (!expr || !expr.trim()) return undefined
    try {
      return tryEvaluateExpression(expr, variables)
    } catch (e) {
      if (e instanceof ExpressionError) return undefined
      return undefined
    }
  }

  const visEval = safeEval(row.def.visibleIf)
  const enaEval = safeEval(row.def.enabledIf)
  const warnEval = safeEval(row.def.warning)

  const hidden  = visEval !== undefined && !isTruthy(visEval)
  const disabled = enaEval !== undefined && !isTruthy(enaEval)

  let warning: string | null = null
  if (warnEval !== undefined) {
    if (typeof warnEval === 'string' && warnEval.trim().length > 0) warning = warnEval
    else if (warnEval === true) warning = 'Warning rule triggered.'
  }

  return {
    name: row.name,
    hidden,
    disabled,
    discarded: hidden || disabled,
    warning,
  }
}
