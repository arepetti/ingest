import { useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import {
  Accordion, AccordionHeader, AccordionItem, AccordionPanel,
  Badge, Body1, Button, Card, CardHeader, Checkbox, Dialog, DialogActions,
  DialogBody, DialogContent, DialogSurface, DialogTitle, Divider,
  Dropdown, Field, Input, MessageBar, MessageBarBody, MessageBarTitle,
  Option, Radio, RadioGroup, Spinner, Textarea, Title2, Toolbar, ToolbarButton, Tooltip,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowLeft20Regular, Delete20Regular, Dismiss16Regular } from '@fluentui/react-icons'
import type {
  Cadence, SchemaLayoutNode, SchemaValue, SchemaValueType, UpsertSchemaRequest,
} from '../api/types'
import { useAccounts, useCreateSchema, useSchemas, useSchemaVersionSnapshot, useSubmissions, useUpdateSchema } from '../api/hooks'
import { formatApiError } from '../api/client'
import { LayoutTreeEditor } from '../components/LayoutTreeEditor'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { cadenceLabel } from '../utils/cadence'
import { confirmDelete } from '../utils/confirm'
import { formatDateTime } from '../utils/format'
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
  dialogOptions: { display: 'flex', flexDirection: 'column', gap: '4px' },
  optionHint: { color: tokens.colorNeutralForeground3, fontSize: '12px', marginLeft: '28px' },
  readOnlyLayout: { opacity: 0.85 },
})

const types: SchemaValueType[] = ['String', 'Integer', 'Number', 'Date', 'Boolean']
// Ordered from short to long so the dropdown reads as a natural progression.
const cadences: Cadence[] = ['Daily', 'Weekly', 'Fortnightly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

/** What to do about the version number when publishing changes to an Enabled schema without a bump. */
type PublishChoice = 'increment' | 'asis' | 'draft' | 'discard'

/**
 * Full-page schema editor. Entry points, all routed here:
 *  - `/schemas/new`                       → blank form (Save creates a schema).
 *  - `/schemas/new` + state               → prefilled from an uploaded JSON file (Save creates).
 *  - `/schemas/:name/edit`                → hydrated from the existing schema (Save updates it).
 *  - `/schemas/:name/versions/:entryId`   → read-only snapshot of a past version (no saving).
 *
 * The read-only overview still lives in the drawer on the listing page; only the create/edit
 * flow was promoted to its own page so the long form (values, layout, rules) gets real estate.
 */
export function SchemaEditPage({ readOnly = false }: { readOnly?: boolean }) {
  const s = useStyles()
  const nav = useNavigate()
  const location = useLocation()
  const { name, entryId } = useParams<{ name?: string; entryId?: string }>()
  // Snapshot view (read-only) vs the regular edit/create flow.
  const isSnapshot = readOnly && !!name && !!entryId
  const isEdit = !!name && !readOnly

  // The audience picker only cares about Service-role accounts (those who submit data); the kind
  // (User vs Application) is irrelevant here.
  const services = useAccounts({ role: 'Service' })
  // Only needed in edit mode, to hydrate the form from the existing schema. The listing page
  // primes this cache, so navigating here from a row is usually instant.
  const schemasQuery = useSchemas(undefined, isEdit)
  // Read-only snapshot of a past version, hydrated from the version-history endpoint.
  const snapshotQuery = useSchemaVersionSnapshot(isSnapshot ? name : undefined, isSnapshot ? entryId : undefined)
  // Submission count for this schema — used to gate the "move back to Draft" publish option.
  const submissionsQuery = useSubmissions({ page: 1, pageSize: 1, schemaName: name }, isEdit && !!name)
  const submissionCount = submissionsQuery.data?.total ?? 0
  const create = useCreateSchema()
  const update = useUpdateSchema()

  const [req, setReq] = useState<UpsertSchemaRequest | null>(null)
  const [schemaId, setSchemaId] = useState<string | undefined>(undefined)
  const [hydrated, setHydrated] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  // Snapshot of the schema as it was when the editor opened — used for the "unsaved changes"
  // detection and to know the originally-published version number.
  const [originalPayload, setOriginalPayload] = useState<string | null>(null)
  const [originalVersion, setOriginalVersion] = useState<number>(1)
  // The publish dialog (only relevant in edit mode for an Enabled schema with no version bump).
  const [versionDialogOpen, setVersionDialogOpen] = useState(false)
  const [publishChoice, setPublishChoice] = useState<PublishChoice>('increment')
  // Names of the values that already existed when the schema was loaded. These are locked from
  // renaming/removal-without-confirmation because validation rules and existing submissions
  // reference them by name. Values added in this session are absent from the set and stay freely
  // editable until saved.
  const [lockedValueNames, setLockedValueNames] = useState<Set<string>>(new Set())

  const existing = isEdit ? schemasQuery.data?.items.find(sc => sc.name === name) : undefined

  // Initialise the form exactly once: from the uploaded JSON (router state) or a blank template
  // for new schemas, from the loaded entity for edits, or from the version snapshot for read-only.
  useEffect(() => {
    if (hydrated) return
    // One-time hydration from router state / loaded entity (async). Guarded by `hydrated`,
    // so this is initialisation, not derived state we could compute during render.
    /* eslint-disable react-hooks/set-state-in-effect */
    if (isSnapshot) {
      if (snapshotQuery.data) {
        const r = toRequest(snapshotQuery.data.schema)
        setReq(r)
        setSchemaId(snapshotQuery.data.schema.id)
        setHydrated(true)
      }
      return
    }
    if (!isEdit) {
      const initial = (location.state as { initialSchema?: UpsertSchemaRequest } | null)?.initialSchema
      setReq(initial ?? emptySchema())
      setHydrated(true)
      return
    }
    if (existing) {
      const r = toRequest(existing)
      setReq(r)
      setSchemaId(existing.id)
      setLockedValueNames(new Set(existing.values.map(v => v.name)))
      setOriginalPayload(JSON.stringify(normalisePayload(r)))
      setOriginalVersion(r.version ?? 1)
      setHydrated(true)
    }
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [hydrated, isEdit, isSnapshot, existing, snapshotQuery.data, location.state])

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

  // Whether the form differs from what was loaded (edit mode only). Read-only/new flows never
  // need this. Compared against the same normalisation the save uses so trailing blank rules,
  // etc. don't count as edits.
  const isDirty = useMemo(() => {
    if (!isEdit || !req || originalPayload === null) return false
    return JSON.stringify(normalisePayload(req)) !== originalPayload
  }, [isEdit, req, originalPayload])

  /** Persist the given request to the server, then return to the listing on success. */
  async function persist(payloadReq: UpsertSchemaRequest) {
    setSubmitError(null)
    const payload = normalisePayload(payloadReq)
    try {
      if (schemaId) await update.mutateAsync({ id: schemaId, req: payload })
      else await create.mutateAsync(payload)
      nav('/schemas')
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  function onSave() {
    if (!req) return
    // Prompt about the version only when an already-published schema was changed without a bump.
    // Creates, drafts, version bumps, and no-op saves go straight through.
    if (isEdit && isDirty && req.enabled && (req.version ?? 1) === originalVersion) {
      setPublishChoice('increment')
      setVersionDialogOpen(true)
      return
    }
    void persist(req)
  }

  function onPublishContinue() {
    if (!req) return
    setVersionDialogOpen(false)
    switch (publishChoice) {
      case 'increment':
        void persist({ ...req, version: originalVersion + 1 })
        break
      case 'asis':
        void persist(req)
        break
      case 'draft':
        void persist({ ...req, enabled: false })
        break
      case 'discard':
        nav('/schemas')
        break
    }
  }

  const isBusy = create.isPending || update.isPending
  // In edit mode we're still resolving the schema from the cache/network.
  const loading = (isEdit && !hydrated && schemasQuery.isLoading) || (isSnapshot && !hydrated && snapshotQuery.isLoading)
  // Finished loading but no schema by that name — bad URL or a since-deleted schema.
  const notFound = (isEdit && !hydrated && !schemasQuery.isLoading && !existing)
    || (isSnapshot && !hydrated && !snapshotQuery.isLoading && !snapshotQuery.data)

  const title = isSnapshot ? 'View schema version' : isEdit ? 'Edit schema' : 'New schema'

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <div className={s.headerLeft}>
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={() => nav(isSnapshot ? `/schemas/${encodeURIComponent(name!)}/versions` : '/schemas')}>Back</Button>
          <Title2>{title}</Title2>
        </div>
        {req && !readOnly && (
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
            {isSnapshot
              ? 'This version snapshot no longer exists. It may have been deleted from the history.'
              : <>No schema named “{name}” exists. It may have been deleted.</>}
          </MessageBarBody>
        </MessageBar>
      )}

      {req && isSnapshot && snapshotQuery.data && (
        <MessageBar intent="info">
          <MessageBarBody>
            <MessageBarTitle>Read-only snapshot</MessageBarTitle>
            Version {snapshotQuery.data.newVersion} saved {formatDateTime(snapshotQuery.data.changeDate)}
            {snapshotQuery.data.authorName ? ` by ${snapshotQuery.data.authorName}` : ''}. This is a
            historical view and cannot be edited — it does not affect the current schema.
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
                <Input value={req.name} disabled={isEdit || readOnly} onChange={(_, v) => patchReq({ name: v.value })} />
              </Field>
              <Field label="Label">
                <Input value={req.label ?? ''} disabled={readOnly} onChange={(_, v) => patchReq({ label: v.value })} />
              </Field>
            </div>

            <Field label="Description">
              <Textarea value={req.description ?? ''} disabled={readOnly} onChange={(_, v) => patchReq({ description: v.value })} />
            </Field>

            <div className={s.flagsRow}>
              <Checkbox label="Enabled (Published)" checked={req.enabled} disabled={readOnly} onChange={(_, d) => patchReq({ enabled: !!d.checked })} />
              <Checkbox label="Modifiable" checked={req.modifiable} disabled={readOnly} onChange={(_, d) => patchReq({ modifiable: !!d.checked })} />
            </div>

            <Field
              label="Version"
              hint="Bump when introducing new values. Cannot decrease. Each value's 'Since version' must be ≤ this."
            >
              <Input
                type="number"
                value={String(req.version ?? 1)}
                disabled={readOnly}
                onChange={(_, v) => {
                  const n = v.value === '' ? 1 : Math.max(0, Math.floor(Number(v.value) || 0))
                  patchReq({ version: n })
                }}
              />
            </Field>

            <div className={s.sectionLabel}>Audience</div>
            <Checkbox label="Global (visible to all services)" checked={req.isGlobal} disabled={readOnly} onChange={(_, d) => patchReq({ isGlobal: !!d.checked })} />
            {!req.isGlobal && (
              <Field label="Visible to services">
                <Dropdown
                  multiselect
                  disabled={readOnly}
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
                    No rules yet. {readOnly ? '' : 'Use “Add rule” to create one.'}
                  </Body1>
                )}
                {req.submissionValidations.map((rule, i) => (
                  <div key={i} className={s.ruleRow}>
                    <Textarea
                      className={s.ruleTextarea}
                      rows={3}
                      value={rule}
                      disabled={readOnly}
                      placeholder="e.g. if(expenses > revenue, 'expenses cannot exceed revenue', null)"
                      onChange={(_, v) => patchValidation(i, v.value)}
                    />
                    {!readOnly && (
                      <Tooltip content="Remove rule" relationship="label">
                        <Button
                          appearance="subtle"
                          icon={<Dismiss16Regular />}
                          onClick={() => removeValidation(i)}
                          aria-label="Remove rule"
                        />
                      </Tooltip>
                    )}
                  </div>
                ))}
                {!readOnly && (
                  <div>
                    <Button appearance="subtle" icon={<Add20Regular />} size="small" onClick={addValidation}>
                      Add rule
                    </Button>
                  </div>
                )}
              </div>
            </Field>

            <Field label="Notes">
              <Textarea value={req.notes ?? ''} disabled={readOnly} onChange={(_, v) => patchReq({ notes: v.value })} />
            </Field>

            <Divider />

            <div className={s.valuesToolbar}>
              <div className={s.sectionLabel} style={{ marginTop: 0 }}>Values</div>
              {!readOnly && <Button appearance="primary" icon={<Add20Regular />} size="small" onClick={addValue}>Add value</Button>}
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
                      disabled={readOnly}
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
              {/* `inert` makes the whole subtree non-interactive in the read-only snapshot view. */}
              <div className={readOnly ? s.readOnlyLayout : undefined} {...(readOnly ? { inert: true } : {})}>
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
              </div>
            </Field>

            {submitError && (
              <AutoScrollMessageBar intent="error">
                <MessageBarBody>{submitError}</MessageBarBody>
              </AutoScrollMessageBar>
            )}

            {!readOnly && (
              <>
                <Divider />
                <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                  <Button onClick={() => nav('/schemas')}>Cancel</Button>
                  <Button appearance="primary" disabled={isBusy} onClick={onSave}>
                    {isEdit ? 'Save changes' : 'Create schema'}
                  </Button>
                </div>
              </>
            )}
          </div>
        </Card>
      )}

      <Dialog open={versionDialogOpen} onOpenChange={(_, d) => setVersionDialogOpen(d.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Publish changes</DialogTitle>
            <DialogContent>
              <Body1 style={{ display: 'block', marginBottom: 12 }}>
                You changed this published schema but didn’t update the version number. What would you
                like to do?
              </Body1>
              <RadioGroup value={publishChoice} onChange={(_, d) => setPublishChoice(d.value as PublishChoice)}>
                <div className={s.dialogOptions}>
                  <Radio value="increment" label={`Automatically increment the version number (to ${originalVersion + 1})`} />
                  <Radio value="asis" label="Publish as-is without changing the version" />
                  <Radio value="draft" label="Move the schema back to Draft and apply the changes" disabled={submissionCount > 0} />
                  {submissionCount > 0 && (
                    <span className={s.optionHint}>
                      Unavailable — {submissionCount} submission{submissionCount === 1 ? '' : 's'} already exist for this schema.
                    </span>
                  )}
                  <Radio value="discard" label="Discard the changes" />
                </div>
              </RadioGroup>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setVersionDialogOpen(false)}>Cancel and keep editing</Button>
              <Button appearance="primary" disabled={isBusy} onClick={onPublishContinue}>Continue</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}

/**
 * Normalise a request the same way the save does: drop completely-blank rules but keep formatting
 * (newlines, indentation) for the rest. Used both for the actual save and for the dirty-state
 * comparison so cosmetic-only edits don't count as changes.
 */
function normalisePayload(req: UpsertSchemaRequest): UpsertSchemaRequest {
  return { ...req, submissionValidations: req.submissionValidations.filter(v => v.trim().length > 0) }
}

function ValueEditor({ value, schemaVersion, nameLocked, disabled, onChange, onRemove }: {
  value: SchemaValue
  /** Parent schema's current `version` — used to bound the "Since version" input. */
  schemaVersion: number
  /** When true the value already exists in the saved schema, so its name is read-only (renaming would break rules/submissions). */
  nameLocked: boolean
  /** When true the whole editor is read-only (snapshot view): inputs disabled, remove hidden. */
  disabled?: boolean
  onChange: (patch: Partial<SchemaValue>) => void
  onRemove: () => void
}) {
  const s = useStyles()
  return (
    <Card className={s.valueCard}>
      <CardHeader
        header={<strong>Value details</strong>}
        action={disabled ? undefined : <Button appearance="subtle" icon={<Delete20Regular />} size="small" onClick={onRemove}>Remove</Button>}
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
          <Input value={value.name} disabled={nameLocked || disabled} onChange={(_, v) => onChange({ name: v.value })} />
        </Field>
        <Field label="Label">
          <Input value={value.label ?? ''} disabled={disabled} onChange={(_, v) => onChange({ label: v.value })} />
        </Field>
      </div>

      <Field label="Description">
        <Textarea value={value.description ?? ''} disabled={disabled} onChange={(_, v) => onChange({ description: v.value })} />
      </Field>

      <Field
        label="Caption"
        hint="Optional heading rendered above this value in the submission form and view (think section title). Display-only; clients ignore it."
      >
        <Input value={value.caption ?? ''} disabled={disabled} onChange={(_, v) => onChange({ caption: v.value })} />
      </Field>

      <Field
        label="Since version"
        hint={`Optional. When set and equal to the schema's current version (${schemaVersion}), the SPA shows a "New" tag next to this value for one cadence period. Leave empty for "always present".`}
      >
        <Input
          type="number"
          disabled={disabled}
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
          <Dropdown value={value.type} disabled={disabled} selectedOptions={[value.type]} onOptionSelect={(_, d) => onChange({ type: d.optionValue as SchemaValueType })}>
            {types.map(t => <Option key={t} value={t}>{t}</Option>)}
          </Dropdown>
        </Field>
        <Field label="Cadence">
          <Dropdown value={value.cadence} disabled={disabled} selectedOptions={[value.cadence]} onOptionSelect={(_, d) => onChange({ cadence: d.optionValue as Cadence })}>
            {cadences.map(c => <Option key={c} value={c} text={cadenceLabel(c)}>{cadenceLabel(c)}</Option>)}
          </Dropdown>
        </Field>
      </div>

      <Field label="Unit">
        <Input value={value.unit ?? ''} disabled={disabled} onChange={(_, v) => onChange({ unit: v.value })} />
      </Field>

      <div className={s.flagsRow}>
        <Checkbox label="Required" checked={value.required} disabled={disabled} onChange={(_, d) => onChange({ required: !!d.checked })} />
        <Checkbox label="Modifiable" checked={value.modifiable} disabled={disabled} onChange={(_, d) => onChange({ modifiable: !!d.checked })} />
        <Checkbox label="Enabled" checked={value.enabled} disabled={disabled} onChange={(_, d) => onChange({ enabled: !!d.checked })} />
      </div>

      {(value.type === 'Integer' || value.type === 'Number') && (
        <>
          <div className={s.sectionLabel}>Numeric constraints</div>
          <div className={s.twoCol}>
            <Field label="Min">
              <Input type="number" disabled={disabled} value={value.min?.toString() ?? ''} onChange={(_, v) => onChange({ min: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label="Max">
              <Input type="number" disabled={disabled} value={value.max?.toString() ?? ''} onChange={(_, v) => onChange({ max: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
        </>
      )}

      {value.type === 'Date' && (
        <>
          <div className={s.sectionLabel}>Date constraints</div>
          <div className={s.twoCol}>
            <Field label="Min date (ISO)">
              <Input disabled={disabled} value={value.minDate ?? ''} onChange={(_, v) => onChange({ minDate: v.value || null })} />
            </Field>
            <Field label="Max date (ISO)">
              <Input disabled={disabled} value={value.maxDate ?? ''} onChange={(_, v) => onChange({ maxDate: v.value || null })} />
            </Field>
          </div>
        </>
      )}

      {value.type === 'String' && (
        <>
          <div className={s.sectionLabel}>String constraints</div>
          <div className={s.twoCol}>
            <Field label="Min length">
              <Input type="number" disabled={disabled} value={value.minLength?.toString() ?? ''} onChange={(_, v) => onChange({ minLength: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label="Max length">
              <Input type="number" disabled={disabled} value={value.maxLength?.toString() ?? ''} onChange={(_, v) => onChange({ maxLength: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
          <Field label="Regex pattern">
            <Input disabled={disabled} value={value.regexPattern ?? ''} onChange={(_, v) => onChange({ regexPattern: v.value })} />
          </Field>
        </>
      )}

      <div className={s.sectionLabel}>Validation</div>
      <RuleTextarea
        label="Value validation"
        hint="Runs against the submitted sample. Vars include value, minimum, maximum. Whitespace and line breaks are ignored."
        rows={3}
        disabled={disabled}
        value={value.valueValidation ?? ''}
        onChange={(v) => onChange({ valueValidation: v })}
      />
      <RuleTextarea
        label="Warning"
        hint="Optional rule that produces a non-blocking warning when true or when it returns a non-empty string."
        rows={3}
        disabled={disabled}
        value={value.warning ?? ''}
        onChange={(v) => onChange({ warning: v })}
      />

      <div className={s.sectionLabel}>Conditional display</div>
      <RuleTextarea
        label="Enabled if"
        hint="When false (or null) the value is disabled in the UI and a submitted sample is dropped with a warning. Empty = always enabled."
        rows={2}
        disabled={disabled}
        value={value.enabledIf ?? ''}
        onChange={(v) => onChange({ enabledIf: v })}
      />
      <RuleTextarea
        label="Visible if"
        hint="When false (or null) the value is hidden in the UI. Server-side behaves like Enabled if. Empty = always visible."
        rows={2}
        disabled={disabled}
        value={value.visibleIf ?? ''}
        onChange={(v) => onChange({ visibleIf: v })}
      />

      <Field label="Notes">
        <Textarea value={value.notes ?? ''} disabled={disabled} onChange={(_, v) => onChange({ notes: v.value })} />
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
  label, hint, rows, value, disabled, onChange,
}: {
  label: string
  hint?: string
  rows?: number
  value: string
  disabled?: boolean
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
      <Textarea rows={rows} value={value} disabled={disabled} onChange={(_, v) => onChange(v.value)} />
    </Field>
  )
}
