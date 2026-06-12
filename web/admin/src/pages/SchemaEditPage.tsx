import { useEffect, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import {
  Accordion, AccordionHeader, AccordionItem, AccordionPanel,
  Badge, Body1, Button, Card, CardHeader, Checkbox, Divider,
  Dropdown, Field, Input, MessageBar, MessageBarBody, MessageBarTitle,
  Option, Spinner, Textarea, Title2, Toolbar, ToolbarButton, Tooltip,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowLeft20Regular, Delete20Regular, Dismiss16Regular } from '@fluentui/react-icons'
import type {
  Cadence, SchemaLayoutNode, SchemaValue, SchemaValueType, UpsertSchemaRequest,
} from '../api/types'
import { useAccounts, useCreateSchema, useSchemas, useUpdateSchema } from '../api/hooks'
import { formatApiError } from '../api/client'
import { LayoutTreeEditor } from '../components/LayoutTreeEditor'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { cadenceLabel } from '../utils/cadence'
import { confirmDelete } from '../utils/confirm'
import { validateExpression, type ExpressionSyntaxResult } from '../utils/expression'
import { emptySchema, emptyValue, isValidValueName, toRequest } from '../utils/schema'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  headerLeft: { display: 'flex', alignItems: 'center', gap: '12px' },
  form: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', alignItems: 'start' },
  flagsRow: { display: 'flex', flexWrap: 'wrap', gap: '16px', alignItems: 'center' },
  sectionLabel: { color: tokens.colorNeutralForeground3, fontWeight: 600, fontSize: '12px', textTransform: 'uppercase', marginTop: '12px' },
  valuesToolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  valueCard: { padding: '12px', backgroundColor: tokens.colorNeutralBackground2, borderRadius: '6px' },
  valueHeader: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' },
  rulesList: { display: 'flex', flexDirection: 'column', gap: '8px' },
  ruleRow: { display: 'flex', alignItems: 'flex-start', gap: '8px' },
  ruleTextarea: { flex: 1 },
})

const types: SchemaValueType[] = ['String', 'Integer', 'Number', 'Date', 'Boolean']
// Ordered from short to long so the dropdown reads as a natural progression.
const cadences: Cadence[] = ['Daily', 'Weekly', 'Fortnightly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

/**
 * Full-page schema editor. Three entry points, all routed here:
 *  - `/schemas/new`            → blank form (Save creates a schema).
 *  - `/schemas/new` + state    → prefilled from an uploaded JSON file (Save creates).
 *  - `/schemas/:name/edit`     → hydrated from the existing schema (Save updates it).
 *
 * The read-only overview still lives in the drawer on the listing page; only the create/edit
 * flow was promoted to its own page so the long form (values, layout, rules) gets real estate.
 */
export function SchemaEditPage() {
  const s = useStyles()
  const nav = useNavigate()
  const location = useLocation()
  const { name } = useParams<{ name?: string }>()
  const isEdit = !!name

  // The audience picker only cares about Service-role accounts (those who submit data); the kind
  // (User vs Application) is irrelevant here.
  const services = useAccounts({ role: 'Service' })
  // Only needed in edit mode, to hydrate the form from the existing schema. The listing page
  // primes this cache, so navigating here from a row is usually instant.
  const schemasQuery = useSchemas(undefined, isEdit)
  const create = useCreateSchema()
  const update = useUpdateSchema()

  const [req, setReq] = useState<UpsertSchemaRequest | null>(null)
  const [schemaId, setSchemaId] = useState<string | undefined>(undefined)
  const [hydrated, setHydrated] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  // Names of the values that already existed when the schema was loaded. These are locked from
  // renaming/removal-without-confirmation because validation rules and existing submissions
  // reference them by name. Values added in this session are absent from the set and stay freely
  // editable until saved.
  const [lockedValueNames, setLockedValueNames] = useState<Set<string>>(new Set())

  const existing = isEdit ? schemasQuery.data?.items.find(sc => sc.name === name) : undefined

  // Initialise the form exactly once: from the uploaded JSON (router state) or a blank template
  // for new schemas, or from the loaded entity for edits.
  useEffect(() => {
    if (hydrated) return
    // One-time hydration from router state / loaded entity (async). Guarded by `hydrated`,
    // so this is initialisation, not derived state we could compute during render.
    /* eslint-disable react-hooks/set-state-in-effect */
    if (!isEdit) {
      const initial = (location.state as { initialSchema?: UpsertSchemaRequest } | null)?.initialSchema
      setReq(initial ?? emptySchema())
      setHydrated(true)
      return
    }
    if (existing) {
      setReq(toRequest(existing))
      setSchemaId(existing.id)
      setLockedValueNames(new Set(existing.values.map(v => v.name)))
      setHydrated(true)
    }
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [hydrated, isEdit, existing, location.state])

  function patchReq(patch: Partial<UpsertSchemaRequest>) {
    setReq(prev => (prev ? { ...prev, ...patch } : prev))
  }

  function patchValue(index: number, patch: Partial<SchemaValue>) {
    setReq(prev => prev ? { ...prev, values: prev.values.map((v, i) => i === index ? { ...v, ...patch } : v) } : prev)
  }
  function addValue() {
    setReq(prev => prev ? { ...prev, values: [...prev.values, emptyValue()] } : prev)
  }
  function removeValue(index: number) {
    const target = req?.values[index]
    // Only existing (already-saved) values carry the "might break things" risk; freshly-added
    // values can be dropped silently. For existing ones, warn and steer toward disabling instead.
    const isExisting = !!target && lockedValueNames.has(target.name)
    if (isExisting) {
      const ok = confirmDelete(
        'value',
        target!.label || target!.name,
        'Removing a value can break validation rules and reject or alter existing submissions. ' +
          'Consider disabling it instead (uncheck "Enabled") to keep historical data intact.\n\nRemove it anyway?',
      )
      if (!ok) return
    }
    setReq(prev => prev ? { ...prev, values: prev.values.filter((_, i) => i !== index) } : prev)
  }

  function patchValidation(index: number, text: string) {
    setReq(prev => prev ? { ...prev, submissionValidations: prev.submissionValidations.map((v, i) => i === index ? text : v) } : prev)
  }
  function addValidation() {
    setReq(prev => prev ? { ...prev, submissionValidations: [...prev.submissionValidations, ''] } : prev)
  }
  function removeValidation(index: number) {
    setReq(prev => prev ? { ...prev, submissionValidations: prev.submissionValidations.filter((_, i) => i !== index) } : prev)
  }

  function patchLayout(layout: SchemaLayoutNode[]) {
    patchReq({ layout })
  }

  async function onSave() {
    if (!req) return
    setSubmitError(null)
    // Drop completely-blank rules but keep formatting (newlines, indentation) for the rest.
    const payload: UpsertSchemaRequest = {
      ...req,
      submissionValidations: req.submissionValidations.filter(v => v.trim().length > 0),
    }
    try {
      if (schemaId) await update.mutateAsync({ id: schemaId, req: payload })
      else await create.mutateAsync(payload)
      nav('/schemas')
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  const isBusy = create.isPending || update.isPending
  // In edit mode we're still resolving the schema from the cache/network.
  const loading = isEdit && !hydrated && schemasQuery.isLoading
  // Finished loading but no schema by that name — bad URL or a since-deleted schema.
  const notFound = isEdit && !hydrated && !schemasQuery.isLoading && !existing

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <div className={s.headerLeft}>
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={() => nav('/schemas')}>Back</Button>
          <Title2>{isEdit ? 'Edit schema' : 'New schema'}</Title2>
        </div>
        {req && (
          <Toolbar>
            <ToolbarButton appearance="primary" disabled={isBusy} onClick={onSave}>
              {isEdit ? 'Save changes' : 'Create schema'}
            </ToolbarButton>
          </Toolbar>
        )}
      </div>

      {loading && <Spinner label="Loading schema…" />}
      {notFound && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Schema not found</MessageBarTitle>
            No schema named “{name}” exists. It may have been deleted.
          </MessageBarBody>
        </MessageBar>
      )}

      {req && (
        <Card>
          <div className={s.form}>
            <div className={s.twoCol}>
              <Field
                label="Name"
                required
                hint={isEdit ? 'The schema name is fixed after creation — changing it could break validation rules and existing submissions.' : undefined}
              >
                <Input value={req.name} disabled={isEdit} onChange={(_, v) => patchReq({ name: v.value })} />
              </Field>
              <Field label="Label">
                <Input value={req.label ?? ''} onChange={(_, v) => patchReq({ label: v.value })} />
              </Field>
            </div>

            <Field label="Description">
              <Textarea value={req.description ?? ''} onChange={(_, v) => patchReq({ description: v.value })} />
            </Field>

            <div className={s.flagsRow}>
              <Checkbox label="Enabled" checked={req.enabled} onChange={(_, d) => patchReq({ enabled: !!d.checked })} />
              <Checkbox label="Modifiable" checked={req.modifiable} onChange={(_, d) => patchReq({ modifiable: !!d.checked })} />
            </div>

            <Field
              label="Version"
              hint="Bump when introducing new values. Cannot decrease. Each value's 'Since version' must be ≤ this."
            >
              <Input
                type="number"
                value={String(req.version ?? 1)}
                onChange={(_, v) => {
                  const n = v.value === '' ? 1 : Math.max(0, Math.floor(Number(v.value) || 0))
                  patchReq({ version: n })
                }}
              />
            </Field>

            <div className={s.sectionLabel}>Audience</div>
            <Checkbox label="Global (visible to all services)" checked={req.isGlobal} onChange={(_, d) => patchReq({ isGlobal: !!d.checked })} />
            {!req.isGlobal && (
              <Field label="Visible to services">
                <Dropdown
                  multiselect
                  selectedOptions={req.serviceIds}
                  value={(services.data?.items ?? []).filter(a => req.serviceIds.includes(a.id)).map(a => a.label || a.name).join(', ')}
                  onOptionSelect={(_, d) => patchReq({ serviceIds: d.selectedOptions })}
                >
                  {(services.data?.items ?? []).map(a => (
                    <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
                  ))}
                </Dropdown>
              </Field>
            )}

            <div className={s.sectionLabel}>Submission validations</div>
            <Field hint="Each rule runs once per submission against every value in the schema. Add several to enforce multiple cross-value invariants.">
              <div className={s.rulesList}>
                {req.submissionValidations.length === 0 && (
                  <Body1 style={{ color: tokens.colorNeutralForeground3, fontSize: 12 }}>
                    No rules yet. Use “Add rule” to create one.
                  </Body1>
                )}
                {req.submissionValidations.map((rule, i) => (
                  <div key={i} className={s.ruleRow}>
                    <Textarea
                      className={s.ruleTextarea}
                      rows={3}
                      value={rule}
                      placeholder="e.g. if(expenses > revenue, 'expenses cannot exceed revenue', null)"
                      onChange={(_, v) => patchValidation(i, v.value)}
                    />
                    <Tooltip content="Remove rule" relationship="label">
                      <Button
                        appearance="subtle"
                        icon={<Dismiss16Regular />}
                        onClick={() => removeValidation(i)}
                        aria-label="Remove rule"
                      />
                    </Tooltip>
                  </div>
                ))}
                <div>
                  <Button appearance="subtle" icon={<Add20Regular />} size="small" onClick={addValidation}>
                    Add rule
                  </Button>
                </div>
              </div>
            </Field>

            <Field label="Notes">
              <Textarea value={req.notes ?? ''} onChange={(_, v) => patchReq({ notes: v.value })} />
            </Field>

            <Divider />

            <div className={s.valuesToolbar}>
              <div className={s.sectionLabel} style={{ marginTop: 0 }}>Values</div>
              <Button appearance="primary" icon={<Add20Regular />} size="small" onClick={addValue}>Add value</Button>
            </div>

            {req.values.length === 0 && (
              <MessageBar intent="info">
                <MessageBarBody>A schema needs at least one value to be useful.</MessageBarBody>
              </MessageBar>
            )}

            <Accordion multiple collapsible>
              {req.values.map((v, i) => (
                <AccordionItem key={i} value={String(i)}>
                  <AccordionHeader>
                    <div className={s.valueHeader}>
                      <span>
                        <strong>{v.label || v.name || `(value #${i + 1})`}</strong>
                        {v.name && <span style={{ color: '#888' }}> · {v.name}</span>}
                        {' '}<Badge appearance="outline" color={v.enabled ? 'success' : 'subtle'} size="small">{v.type}</Badge>
                        {' '}<Badge appearance="outline" color="informative" size="small">{cadenceLabel(v.cadence)}</Badge>
                        {v.required && <> <Badge appearance="outline" color="severe" size="small">required</Badge></>}
                      </span>
                    </div>
                  </AccordionHeader>
                  <AccordionPanel>
                    <ValueEditor
                      value={v}
                      schemaVersion={req.version ?? 1}
                      nameLocked={lockedValueNames.has(v.name)}
                      onChange={patch => patchValue(i, patch)}
                      onRemove={() => removeValue(i)}
                    />
                  </AccordionPanel>
                </AccordionItem>
              ))}
            </Accordion>

            <Divider />

            <div className={s.sectionLabel}>Layout (UI grouping)</div>
            <Field hint="Drag values into sections to group them. This affects the submission form layout only — the server treats submissions as flat lists.">
              <LayoutTreeEditor
                schema={{
                  // Synthesise just enough of a Schema for the editor's needs. Pulling the
                  // actual entity in would force us to round-trip through the server for IDs
                  // we don't need at edit time.
                  id: schemaId ?? '',
                  name: req.name,
                  label: req.label,
                  description: req.description,
                  notes: req.notes,
                  modifiable: req.modifiable,
                  enabled: req.enabled,
                  submissionValidations: req.submissionValidations,
                  isGlobal: req.isGlobal,
                  serviceIds: req.serviceIds,
                  values: req.values,
                  layout: req.layout ?? [],
                  version: req.version ?? 1,
                  versionModifiedAt: null,
                  createdAt: '', modifiedAt: '',
                }}
                onChange={patchLayout}
              />
            </Field>

            {submitError && (
              <AutoScrollMessageBar intent="error">
                <MessageBarBody>{submitError}</MessageBarBody>
              </AutoScrollMessageBar>
            )}

            <Divider />
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <Button onClick={() => nav('/schemas')}>Cancel</Button>
              <Button appearance="primary" disabled={isBusy} onClick={onSave}>
                {isEdit ? 'Save changes' : 'Create schema'}
              </Button>
            </div>
          </div>
        </Card>
      )}
    </div>
  )
}

function ValueEditor({ value, schemaVersion, nameLocked, onChange, onRemove }: {
  value: SchemaValue
  /** Parent schema's current `version` — used to bound the "Since version" input. */
  schemaVersion: number
  /** When true the value already exists in the saved schema, so its name is read-only (renaming would break rules/submissions). */
  nameLocked: boolean
  onChange: (patch: Partial<SchemaValue>) => void
  onRemove: () => void
}) {
  const s = useStyles()
  return (
    <Card className={s.valueCard}>
      <CardHeader
        header={<strong>Value details</strong>}
        action={<Button appearance="subtle" icon={<Delete20Regular />} size="small" onClick={onRemove}>Remove</Button>}
      />

      <div className={s.twoCol}>
        <Field
          label="Name"
          required
          hint={nameLocked
            ? 'The name is fixed after creation — it is referenced by validation rules and existing submissions. Add a new value if you need a different name.'
            : 'Used as the identifier in validation rules. Must start with a letter or underscore and contain only letters, digits, and underscores.'}
          validationState={nameLocked || isValidValueName(value.name) ? 'none' : 'error'}
          validationMessage={nameLocked || isValidValueName(value.name) ? undefined : 'Must be a valid identifier: letters, digits and underscores only; cannot start with a digit.'}
        >
          <Input value={value.name} disabled={nameLocked} onChange={(_, v) => onChange({ name: v.value })} />
        </Field>
        <Field label="Label">
          <Input value={value.label ?? ''} onChange={(_, v) => onChange({ label: v.value })} />
        </Field>
      </div>

      <Field label="Description">
        <Textarea value={value.description ?? ''} onChange={(_, v) => onChange({ description: v.value })} />
      </Field>

      <Field
        label="Caption"
        hint="Optional heading rendered above this value in the submission form and view (think section title). Display-only; clients ignore it."
      >
        <Input value={value.caption ?? ''} onChange={(_, v) => onChange({ caption: v.value })} />
      </Field>

      <Field
        label="Since version"
        hint={`Optional. When set and equal to the schema's current version (${schemaVersion}), the SPA shows a "New" tag next to this value for one cadence period. Leave empty for "always present".`}
      >
        <Input
          type="number"
          value={value.sinceVersion === null || value.sinceVersion === undefined ? '' : String(value.sinceVersion)}
          onChange={(_, v) => {
            if (v.value === '') return onChange({ sinceVersion: null })
            const n = Math.max(0, Math.floor(Number(v.value) || 0))
            onChange({ sinceVersion: Math.min(n, schemaVersion) })
          }}
        />
      </Field>

      <div className={s.twoCol}>
        <Field label="Type">
          <Dropdown value={value.type} selectedOptions={[value.type]} onOptionSelect={(_, d) => onChange({ type: d.optionValue as SchemaValueType })}>
            {types.map(t => <Option key={t} value={t}>{t}</Option>)}
          </Dropdown>
        </Field>
        <Field label="Cadence">
          <Dropdown value={value.cadence} selectedOptions={[value.cadence]} onOptionSelect={(_, d) => onChange({ cadence: d.optionValue as Cadence })}>
            {cadences.map(c => <Option key={c} value={c} text={cadenceLabel(c)}>{cadenceLabel(c)}</Option>)}
          </Dropdown>
        </Field>
      </div>

      <Field label="Unit">
        <Input value={value.unit ?? ''} onChange={(_, v) => onChange({ unit: v.value })} />
      </Field>

      <div className={s.flagsRow}>
        <Checkbox label="Required" checked={value.required} onChange={(_, d) => onChange({ required: !!d.checked })} />
        <Checkbox label="Modifiable" checked={value.modifiable} onChange={(_, d) => onChange({ modifiable: !!d.checked })} />
        <Checkbox label="Enabled" checked={value.enabled} onChange={(_, d) => onChange({ enabled: !!d.checked })} />
      </div>

      {(value.type === 'Integer' || value.type === 'Number') && (
        <>
          <div className={s.sectionLabel}>Numeric constraints</div>
          <div className={s.twoCol}>
            <Field label="Min">
              <Input type="number" value={value.min?.toString() ?? ''} onChange={(_, v) => onChange({ min: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label="Max">
              <Input type="number" value={value.max?.toString() ?? ''} onChange={(_, v) => onChange({ max: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
        </>
      )}

      {value.type === 'Date' && (
        <>
          <div className={s.sectionLabel}>Date constraints</div>
          <div className={s.twoCol}>
            <Field label="Min date (ISO)">
              <Input value={value.minDate ?? ''} onChange={(_, v) => onChange({ minDate: v.value || null })} />
            </Field>
            <Field label="Max date (ISO)">
              <Input value={value.maxDate ?? ''} onChange={(_, v) => onChange({ maxDate: v.value || null })} />
            </Field>
          </div>
        </>
      )}

      {value.type === 'String' && (
        <>
          <div className={s.sectionLabel}>String constraints</div>
          <div className={s.twoCol}>
            <Field label="Min length">
              <Input type="number" value={value.minLength?.toString() ?? ''} onChange={(_, v) => onChange({ minLength: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label="Max length">
              <Input type="number" value={value.maxLength?.toString() ?? ''} onChange={(_, v) => onChange({ maxLength: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
          <Field label="Regex pattern">
            <Input value={value.regexPattern ?? ''} onChange={(_, v) => onChange({ regexPattern: v.value })} />
          </Field>
        </>
      )}

      <div className={s.sectionLabel}>Validation</div>
      <RuleTextarea
        label="Value validation"
        hint="Runs against the submitted sample. Vars include value, minimum, maximum. Whitespace and line breaks are ignored."
        rows={3}
        value={value.valueValidation ?? ''}
        onChange={(v) => onChange({ valueValidation: v })}
      />
      <RuleTextarea
        label="Warning"
        hint="Optional rule that produces a non-blocking warning when true or when it returns a non-empty string."
        rows={3}
        value={value.warning ?? ''}
        onChange={(v) => onChange({ warning: v })}
      />

      <div className={s.sectionLabel}>Conditional display</div>
      <RuleTextarea
        label="Enabled if"
        hint="When false (or null) the value is disabled in the UI and a submitted sample is dropped with a warning. Empty = always enabled."
        rows={2}
        value={value.enabledIf ?? ''}
        onChange={(v) => onChange({ enabledIf: v })}
      />
      <RuleTextarea
        label="Visible if"
        hint="When false (or null) the value is hidden in the UI. Server-side behaves like Enabled if. Empty = always visible."
        rows={2}
        value={value.visibleIf ?? ''}
        onChange={(v) => onChange({ visibleIf: v })}
      />

      <Field label="Notes">
        <Textarea value={value.notes ?? ''} onChange={(_, v) => onChange({ notes: v.value })} />
      </Field>
    </Card>
  )
}

/**
 * Textarea that asks the server to syntax-check the entered expression on every change
 * (debounced) and surfaces the result inline. Empty input is treated as "no rule" and never
 * flagged. The component is intentionally tolerant of network hiccups — a transport failure
 * just clears the indicator rather than nagging the user with red squiggles for transient
 * issues.
 */
function RuleTextarea({
  label, hint, rows, value, onChange,
}: {
  label: string
  hint?: string
  rows?: number
  value: string
  onChange: (next: string) => void
}) {
  const [status, setStatus] = useState<'idle' | 'checking' | ExpressionSyntaxResult>('idle')

  useEffect(() => {
    const trimmed = value.trim()
    // Debounced async syntax check; driving the status indicator is the effect's purpose.
    /* eslint-disable react-hooks/set-state-in-effect */
    if (!trimmed) { setStatus('idle'); return }
    setStatus('checking')
    /* eslint-enable react-hooks/set-state-in-effect */
    let cancelled = false
    // Small debounce so we don't translate-validate on every keystroke.
    const t = window.setTimeout(() => {
      validateExpression(trimmed).then((r) => { if (!cancelled) setStatus(r) })
    }, 250)
    return () => { cancelled = true; window.clearTimeout(t) }
  }, [value])

  // Fluent UI's `Field` decides which glyph to render based on `validationState`. If we pass
  // `validationMessage` without a state it defaults to "error", which is why "Valid syntax"
  // used to appear next to a red ✕ — the message text and the icon disagreed. Mirror the
  // server's verdict here so the glyph and the message always tell the same story.
  let validationState: 'success' | 'error' | 'none' = 'none'
  let validationMessage: string | undefined
  if (status !== 'idle' && status !== 'checking') {
    if (status.ok) {
      validationState = 'success'
      validationMessage = 'Valid syntax'
    } else {
      validationState = 'error'
      validationMessage = `Syntax error: ${status.error}`
    }
  }

  return (
    <Field label={label} hint={hint} validationState={validationState} validationMessage={validationMessage}>
      <Textarea rows={rows} value={value} onChange={(_, v) => onChange(v.value)} />
    </Field>
  )
}
