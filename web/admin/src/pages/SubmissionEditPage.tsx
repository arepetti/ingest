import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  Button, Card, Dropdown, Field, Input,
  MessageBar, MessageBarBody, MessageBarTitle,
  Option, Title2, Toolbar, ToolbarButton,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { ArrowLeft20Regular } from '@fluentui/react-icons'
import {
  useAccounts, useAdminCreateSubmission, useAdminUpdateSubmission,
  useCreateMySubmission, useCapabilities, useMySchemas, useMySubmission,
  useReplaceMySubmission, useSchemas, useSubmission,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import type {
  AdminSubmissionInput, SampleInput, Schema,
} from '../api/types'
import { AccountAvatar, SchemaAvatar } from '../components/Avatars'
import { SchemaSampleFields, fromLocalInput, toLocalInput } from '../components/SchemaSampleFields'
import { isFilled, useSampleRules, type ValueRow } from '../utils/sampleRules'
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
})

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

  const { me, has } = useCapabilities()
  // Self-service submitters (no cross-service read) use their own schemas + /api/submissions.
  const isService = !has('submissions:read')

  // Different data sources depending on capability: self-service callers can only see their own
  // visible schemas and use /api/submissions; everyone else uses the admin listings.
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

  // For services the service is always "me" — pin it.
  useEffect(() => {
    // Pin once `me` resolves (async); not available during the initial render.
    // eslint-disable-next-line react-hooks/set-state-in-effect
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
    // Rebuild/merge the row set when the chosen schema changes, preserving in-progress edits.
    /* eslint-disable react-hooks/set-state-in-effect */
    if (!schema) { setRows([]); return }
    setRows(prev => {
      const byName = new Map(prev.map(r => [r.name, r] as const))
      return schema.values.map(v => byName.get(v.name) ?? { name: v.name, def: v, value: null, note: '' })
    })
    setMissingRequired([])
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [schema])

  // On edit, hydrate state from the existing submission once it has loaded.
  useEffect(() => {
    if (!isEdit || prefilled) return
    const data = existing.data
    if (!data) return

    // One-time hydration from the loaded submission (async). Guarded by `prefilled`.
    /* eslint-disable react-hooks/set-state-in-effect */
    setServiceId(data.serviceAccountId)
    // The current model allows a submission to mix multiple schemas; for editing we lock to the one
    // most of its samples belong to (with a soft warning shown in the UI for the multi-schema case).
    const firstSchema = data.samples[0]?.schemaName
    if (firstSchema) setSchemaName(firstSchema)
    if (data.samples[0]?.timestamp) setTimestamp(data.samples[0].timestamp)
    setPrefilled(true)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [isEdit, prefilled, existing.data])

  // Once we've prefilled and the schema is resolved, fill the row values from the existing samples.
  useEffect(() => {
    if (!isEdit || !prefilled || !schema || !existing.data) return
    const samplesByValue = new Map(
      existing.data.samples
        .filter(s => s.schemaName === schema.name)
        .map(s => [s.valueName, s] as const),
    )
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setRows(prev => prev.map(r => {
      const ex = samplesByValue.get(r.name)
      if (!ex) return r
      return { ...r, value: ex.value, note: ex.note ?? '' }
    }))
  // We deliberately key on schema.name (not the object identity) so swapping schemas re-runs this.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isEdit, prefilled, schema?.name, existing.data])

  function patchRow(name: string, patch: Partial<ValueRow>) {
    setRows(rs => rs.map(r => r.name === name ? { ...r, ...patch } : r))
  }

  // Prefetch + evaluate the schema's rules client-side (mirrors the server) so EnabledIf /
  // VisibleIf hide/grey values and Warning rules surface inline as the user types.
  const { rowStates } = useSampleRules(schema, rows)

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

          <SchemaSampleFields
            schema={schema}
            rows={rows}
            rowStates={rowStates}
            readOnly={readOnly}
            onPatchRow={patchRow}
          />
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

