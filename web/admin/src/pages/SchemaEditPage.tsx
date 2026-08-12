import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import {
  Badge, Body1, Button, Card, Checkbox, Dialog, DialogActions,
  DialogBody, DialogContent, DialogSurface, DialogTitle, Divider, Drawer, DrawerBody,
  Dropdown, Field, Input, MessageBar, MessageBarBody, MessageBarTitle,
  Option, Radio, RadioGroup, Spinner, Tab, TabList, mergeClasses,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Textarea, Title2, Toolbar, ToolbarButton, Tooltip,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowLeft20Regular, CheckmarkCircle16Regular, Chat20Regular, Delete20Regular, Dismiss16Regular, Edit20Regular, ErrorCircle16Regular, Eye20Regular, FlowchartCircle20Regular } from '@fluentui/react-icons'
import type {
  Account, ApprovalPolicy,
  Cadence, Schema, SchemaLayoutNode, SchemaValue, SchemaValueKind, SchemaValueType, UpsertSchemaRequest,
} from '../api/types'
import { useAccounts, useCapabilities, useCreateSchema, useMe, useOpenCommentCounts, useSchemas, useSchemaVersionSnapshot, useSubmissions, useUpdateSchema } from '../api/hooks'
import { accountHasCapability } from '../api/capabilities'
import { formatApiError } from '../api/client'
import { LayoutTreeEditor } from '../components/LayoutTreeEditor'
import { SchemaPreviewDialog } from '../components/SchemaPreviewDialog'
import { SchemaDependencyGraphDialog } from '../components/SchemaDependencyGraphDialog'
import { SchemaCommentsDrawer } from '../components/SchemaCommentsDrawer'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { LocalizedTime } from '../components/LocalizedTime'
import { cadenceLabel } from '../utils/cadence'
import { ApprovalPolicyEditor } from '../components/ApprovalPolicyEditor'
import { confirmDelete } from '../utils/confirm'
import { validateExpression, type ExpressionSyntaxResult } from '../utils/expression'
import { emptySchema, emptyValue, isValidValueName, toRequest } from '../utils/schema'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { RowActions } from '../components/RowActions'
import { SchemaValueAvatar } from '../components/Avatars'
import { ExpressionField } from '../components/ExpressionField'
import { clickableRowProps } from '../utils/a11y'
import { Trans, useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  headerLeft: { display: 'flex', alignItems: 'center', gap: '12px' },
  form: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  // One tab's worth of fields. Same vertical rhythm as the old single-column form.
  tabPanel: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '8px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', alignItems: 'start' },
  flagsRow: { display: 'flex', flexWrap: 'wrap', gap: '16px', alignItems: 'center' },
  sectionLabel: { color: tokens.colorNeutralForeground3, fontWeight: 600, fontSize: '12px', textTransform: 'uppercase', marginTop: '12px' },
  bandHelp: { color: tokens.colorNeutralForeground3, fontSize: '12px', marginBottom: '4px' },
  valuesToolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  drawerToolbar: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Values are listed in a click-to-edit data grid; a row opens the editor in a right-side drawer.
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  valuesTable: { width: '100%' },
  // max-width:0 + truncate is the table-cell "take leftover width then clip" trick: with the other
  // columns capped below, Name soaks up all the remaining width.
  nameCell: { maxWidth: 0, width: 'auto' },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  // Keep the secondary columns content-sized so they don't steal space from Name.
  colMeta: { width: '120px', whiteSpace: 'nowrap' },
  colFlag: { width: '110px', whiteSpace: 'nowrap' },
  actionsHeader: { width: '56px', textAlign: 'right' },
  actionsCell: { width: '56px', textAlign: 'right' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  rulesList: { display: 'flex', flexDirection: 'column', gap: '8px' },
  ruleRow: { display: 'flex', alignItems: 'flex-start', gap: '8px' },
  ruleTextarea: { flex: 1 },
  // Fixed-height status line under each expression editor: reserving the height stops the layout
  // jumping as the indicator cycles through Checking… → Valid / Syntax error.
  ruleStatus: {
    display: 'flex', alignItems: 'center', gap: '4px',
    minHeight: '16px', marginTop: '4px', fontSize: tokens.fontSizeBase200,
  },
  ruleStatusChecking: { color: tokens.colorNeutralForeground3 },
  ruleStatusOk: { color: tokens.colorPaletteGreenForeground1 },
  ruleStatusError: { color: tokens.colorPaletteRedForeground1 },
  dialogOptions: { display: 'flex', flexDirection: 'column', gap: '4px' },
  optionHint: { color: tokens.colorNeutralForeground3, fontSize: '12px', marginLeft: '28px' },
  readOnlyLayout: { opacity: 0.85 },
  approverList: { display: 'flex', flexDirection: 'column', gap: '4px' },
  approverRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '12px',
    padding: '6px 10px',
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: '6px',
  },
  approverName: { fontWeight: tokens.fontWeightSemibold },
})

const types: SchemaValueType[] = ['String', 'Integer', 'Number', 'Date', 'Boolean']
// Ordered from short to long so the dropdown reads as a natural progression.
const cadences: Cadence[] = ['Daily', 'Weekly', 'Fortnightly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

/** What to do about the version number when publishing changes to an Enabled schema without a bump. */
type PublishChoice = 'increment' | 'asis' | 'draft' | 'discard'

/** The sections the editor is split into. (Approvals is hidden when the workflow is disabled.) */
type SchemaTab = 'general' | 'values' | 'validations' | 'approvals' | 'layout'

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
  const { t } = useTranslation()
  const nav = useNavigate()
  const location = useLocation()
  const { name, entryId } = useParams<{ name?: string; entryId?: string }>()
  // Snapshot view (read-only) vs the regular edit/create flow.
  const isSnapshot = readOnly && !!name && !!entryId
  const isEdit = !!name && !readOnly

  const { data: me } = useMe()
  const { has } = useCapabilities()
  // The approval policy editor only appears when the workflow is switched on server-side.
  const approvalEnabled = !!me?.approvalEnabled
  // The audience picker only cares about Service-role accounts (those who submit data); the kind
  // (User vs Application) is irrelevant here.
  const services = useAccounts({ role: 'Service' })
  // Candidate approvers: any account whose effective capabilities include submissions:approve.
  // Fetched only when the workflow is on.
  const approverAccountsQuery = useAccounts(undefined, approvalEnabled)
  const approverAccounts = useMemo<Account[]>(
    () => (approverAccountsQuery.data?.items ?? []).filter(a => accountHasCapability(a, 'submissions:approve')),
    [approverAccountsQuery.data],
  )
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
  const [previewOpen, setPreviewOpen] = useState(false)
  const [dependenciesOpen, setDependenciesOpen] = useState(false)
  const [commentsOpen, setCommentsOpen] = useState(false)
  // Which editor section is showing. The form is split into three tabs that mirror the old
  // top-to-bottom sections (General settings, Values, Layout).
  const [tab, setTab] = useState<SchemaTab>('general')
  const [schemaId, setSchemaId] = useState<string | undefined>(undefined)
  // Powers the Comments toolbar button's badge; a persisted schema id is required (comments can't
  // attach to a never-saved schema), so this stays disabled until one exists.
  const openCommentCounts = useOpenCommentCounts('Schema', schemaId ? [schemaId] : [], has('comments:read') && !!schemaId)
  const openCommentCount = schemaId ? openCommentCounts.data?.[schemaId] ?? 0 : 0
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
  // Index of the value whose editor drawer is open (null = closed), and whether that drawer is
  // expanded to full width.
  const [editingValueIndex, setEditingValueIndex] = useState<number | null>(null)
  const [valueDrawerExpanded, setValueDrawerExpanded] = useState(false)

  function closeValueDrawer() {
    setEditingValueIndex(null)
    setValueDrawerExpanded(false)
  }

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
    // Append a blank value and immediately open its drawer. `req.values.length` is the index the
    // new value lands at (it's appended), captured before the state update applies.
    const newIndex = req?.values.length ?? 0
    setReq(prev => prev ? { ...prev, values: [...prev.values, emptyValue()] } : prev)
    setEditingValueIndex(newIndex)
    setValueDrawerExpanded(false)
  }
  function removeValue(index: number) {
    const target = req?.values[index]
    // Only existing (already-saved) values carry the "might break things" risk; freshly-added
    // values can be dropped silently. For existing ones, warn and steer toward disabling instead.
    const isExisting = !!target && lockedValueNames.has(target.name)
    if (isExisting) {
      const ok = confirmDelete(
        t('schemasSubmissions.schemaEdit.deleteType'),
        target!.label || target!.name,
        t('schemasSubmissions.schemaEdit.removeValueWarning'),
      )
      if (!ok) return
    }
    setReq(prev => prev ? { ...prev, values: prev.values.filter((_, i) => i !== index) } : prev)
    // Indices shift after removal, so just close the editor rather than trying to track the move.
    closeValueDrawer()
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

  // Approval policy edits. Setting mode to `None` clears the policy back to null so untouched
  // schemas keep the back-compatible "no approval" shape rather than persisting an empty object.
  function patchApproval(patch: Partial<ApprovalPolicy>) {
    setReq(prev => {
      if (!prev) return prev
      const base: ApprovalPolicy = prev.approval ?? { mode: 'None', appliesToSources: 'Both', approvers: [] }
      const next = { ...base, ...patch }
      return { ...prev, approval: next.mode === 'None' && next.approvers.length === 0 ? null : next }
    })
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

  const title = isSnapshot
    ? t('schemasSubmissions.schemaEdit.viewVersionTitle')
    : isEdit
      ? t('schemasSubmissions.schemaEdit.editTitle')
      : t('schemasSubmissions.schemas.new')

  // Synthesise a full `Schema` from the in-progress request so the layout editor and the preview
  // dialog can share one definition. Just enough of a Schema for their needs — pulling the actual
  // entity in would force a round-trip for IDs we don't need at edit time.
  const previewSchema = useMemo<Schema | null>(() => {
    if (!req) return null
    return {
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
    }
  }, [req, schemaId])

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <div className={s.headerLeft}>
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={() => nav(isSnapshot ? `/schemas/${encodeURIComponent(name!)}/versions` : '/schemas')}>{t('schemasSubmissions.common.back')}</Button>
          <Title2>{title}</Title2>
        </div>
        {req && (
          <Toolbar>
            <ToolbarButton icon={<Eye20Regular />} onClick={() => setPreviewOpen(true)}>
              {t('schemasSubmissions.common.preview')}
            </ToolbarButton>
            <ToolbarButton icon={<FlowchartCircle20Regular />} onClick={() => setDependenciesOpen(true)}>
              {t('schemasSubmissions.schemaEdit.dependencies')}
            </ToolbarButton>
            {has('comments:read') && schemaId && (
              <ToolbarButton icon={<Chat20Regular />} onClick={() => setCommentsOpen(true)}>
                {t('schemasSubmissions.comments.title')}
                {openCommentCount > 0 && <Badge appearance="filled" color="informative" size="small" style={{ marginLeft: '6px' }}>{openCommentCount}</Badge>}
              </ToolbarButton>
            )}
            {!readOnly && (
              <ToolbarButton appearance="primary" disabled={isBusy} onClick={onSave}>
                {isEdit ? t('schemasSubmissions.common.saveChanges') : t('schemasSubmissions.schemaEdit.create')}
              </ToolbarButton>
            )}
          </Toolbar>
        )}
      </div>

      {loading && <Spinner label={t('schemasSubmissions.schemaEdit.loading')} />}
      {notFound && (
        <MessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('schemasSubmissions.schemaEdit.notFound')}</MessageBarTitle>
            {isSnapshot
              ? t('schemasSubmissions.schemaEdit.snapshotMissing')
              : t('schemasSubmissions.schemaEdit.schemaMissing', { name })}
          </MessageBarBody>
        </MessageBar>
      )}

      {req && isSnapshot && snapshotQuery.data && (
        <MessageBar intent="info">
          <MessageBarBody>
            <MessageBarTitle>{t('schemasSubmissions.schemaEdit.readOnlySnapshot')}</MessageBarTitle>
            <Trans
              i18nKey="schemasSubmissions.schemaEdit.snapshotHelp"
              values={{
                version: snapshotQuery.data.newVersion,
                author: snapshotQuery.data.authorName
                  ? t('schemasSubmissions.common.by', { name: snapshotQuery.data.authorName })
                  : '',
              }}
              components={{ savedAt: <LocalizedTime value={snapshotQuery.data.changeDate} /> }}
            />
          </MessageBarBody>
        </MessageBar>
      )}

      {req && (
        <Card>
          <div className={s.form}>
            <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as SchemaTab)}>
              <Tab value="general">{t('schemasSubmissions.schemaEdit.tabs.general')}</Tab>
              <Tab value="values">{t('schemasSubmissions.common.values')}</Tab>
              <Tab value="validations">{t('schemasSubmissions.schemaEdit.tabs.validations')}</Tab>
              {approvalEnabled && <Tab value="approvals">{t('schemasSubmissions.schemaEdit.tabs.approvals')}</Tab>}
              <Tab value="layout">{t('schemasSubmissions.schemaEdit.tabs.layout')}</Tab>
            </TabList>

            {tab === 'general' && (
            <div className={s.tabPanel}>
            <div className={s.twoCol}>
              <Field
                label={t('schemasSubmissions.common.name')}
                required
                hint={isEdit ? t('schemasSubmissions.schemaEdit.nameFixed') : undefined}
              >
                <Input value={req.name} disabled={isEdit || readOnly} onChange={(_, v) => patchReq({ name: v.value })} />
              </Field>
              <Field label={t('schemasSubmissions.common.label')}>
                <Input value={req.label ?? ''} disabled={readOnly} onChange={(_, v) => patchReq({ label: v.value })} />
              </Field>
            </div>

            <Field label={t('schemasSubmissions.common.description')}>
              <Textarea value={req.description ?? ''} disabled={readOnly} onChange={(_, v) => patchReq({ description: v.value })} />
            </Field>

            <div className={s.flagsRow}>
              <Checkbox label={t('schemasSubmissions.schemaEdit.enabledPublished')} checked={req.enabled} disabled={readOnly} onChange={(_, d) => patchReq({ enabled: !!d.checked })} />
              <Checkbox label={t('schemasSubmissions.common.modifiable')} checked={req.modifiable} disabled={readOnly} onChange={(_, d) => patchReq({ modifiable: !!d.checked })} />
            </div>

            <Field
              label={t('schemasSubmissions.common.version')}
              hint={t('schemasSubmissions.schemaEdit.versionHint')}
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

            <div className={s.sectionLabel}>{t('schemasSubmissions.schemas.audience')}</div>
            <Checkbox label={t('schemasSubmissions.schemas.globalAudience')} checked={req.isGlobal} disabled={readOnly} onChange={(_, d) => patchReq({ isGlobal: !!d.checked })} />
            {!req.isGlobal && (
              <Field label={t('schemasSubmissions.schemaEdit.visibleServices')}>
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

            <Field label={t('schemasSubmissions.common.notes')}>
              <Textarea value={req.notes ?? ''} disabled={readOnly} onChange={(_, v) => patchReq({ notes: v.value })} />
            </Field>
            </div>
            )}

            {tab === 'validations' && (
            <div className={s.tabPanel}>
            <div className={s.sectionLabel}>{t('schemasSubmissions.schemas.submissionValidations')}</div>
            <Field hint={t('schemasSubmissions.schemaEdit.validationsHint')}>
              <div className={s.rulesList}>
                {req.submissionValidations.length === 0 && (
                  <Body1 style={{ color: tokens.colorNeutralForeground3, fontSize: 12 }}>
                    {readOnly
                      ? t('schemasSubmissions.schemaEdit.noRules')
                      : t('schemasSubmissions.schemaEdit.noRulesAdd')}
                  </Body1>
                )}
                {req.submissionValidations.map((rule, i) => (
                  <div key={i} className={s.ruleRow}>
                    <div className={s.ruleTextarea}>
                      <ExpressionField
                        rows={8}
                        value={rule}
                        disabled={readOnly}
                        lint
                        identifiers={req.values.map(v => v.name)}
                        placeholder={t('schemasSubmissions.schemaEdit.validationPlaceholder')}
                        ariaLabel={t('schemasSubmissions.schemas.validationRule', { number: i + 1 })}
                        onChange={(v) => patchValidation(i, v)}
                      />
                    </div>
                    {!readOnly && (
                      <Tooltip content={t('schemasSubmissions.schemaEdit.removeRule')} relationship="label">
                        <Button
                          appearance="subtle"
                          icon={<Dismiss16Regular />}
                          onClick={() => removeValidation(i)}
                          aria-label={t('schemasSubmissions.schemaEdit.removeRule')}
                        />
                      </Tooltip>
                    )}
                  </div>
                ))}
                {!readOnly && (
                  <div>
                    <Button appearance="subtle" icon={<Add20Regular />} size="small" onClick={addValidation}>
                      {t('schemasSubmissions.schemaEdit.addRule')}
                    </Button>
                  </div>
                )}
              </div>
            </Field>
            </div>
            )}

            {approvalEnabled && tab === 'approvals' && (
            <div className={s.tabPanel}>
              <ApprovalPolicyEditor
                policy={req.approval ?? null}
                accounts={approverAccounts}
                modifiableWarning={req.modifiable}
                disabled={readOnly}
                onChange={patchApproval}
              />
            </div>
            )}

            {tab === 'values' && (
            <div className={s.tabPanel}>
            <div className={s.valuesToolbar} style={{ justifyContent: 'flex-end' }}>
              {!readOnly && <Button appearance="primary" icon={<Add20Regular />} size="small" onClick={addValue}>{t('schemasSubmissions.schemaEdit.addValue')}</Button>}
            </div>

            {req.values.length === 0 ? (
              <MessageBar intent="info">
                <MessageBarBody>{t('schemasSubmissions.schemaEdit.needsValue')}</MessageBarBody>
              </MessageBar>
            ) : (
              <Table size="small" className={s.valuesTable}>
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>{t('schemasSubmissions.common.name')}</TableHeaderCell>
                    <TableHeaderCell className={s.colMeta}>{t('schemasSubmissions.common.type')}</TableHeaderCell>
                    <TableHeaderCell className={s.colMeta}>{t('schemasSubmissions.common.cadenceLabel')}</TableHeaderCell>
                    <TableHeaderCell className={s.colMeta}>{t('schemasSubmissions.schemaEdit.kind')}</TableHeaderCell>
                    <TableHeaderCell className={s.colFlag}>{t('schemasSubmissions.common.enabled')}</TableHeaderCell>
                    <TableHeaderCell className={s.colFlag}>{t('schemasSubmissions.sampleFields.required')}</TableHeaderCell>
                    {!readOnly && <TableHeaderCell className={s.actionsHeader}>{t('schemasSubmissions.common.actions')}</TableHeaderCell>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {req.values.map((v, i) => {
                    const display = v.label || v.name || t('schemasSubmissions.schemaEdit.valueNumber', { number: i + 1 })
                    return (
                      <TableRow
                        key={i}
                        className={`${s.row} ${s.rowClickable}`}
                        {...clickableRowProps(() => { setEditingValueIndex(i); setValueDrawerExpanded(false) }, t('schemasSubmissions.schemaEdit.editValue', { name: display }))}
                      >
                        <TableCell className={s.nameCell}>
                          <TableCellLayout media={<SchemaValueAvatar type={v.type} enabled={v.enabled} />} description={v.label && v.name ? v.name : undefined}>
                            <Tooltip content={display} relationship="label">
                              <strong className={s.truncate}>{display}</strong>
                            </Tooltip>
                          </TableCellLayout>
                        </TableCell>
                        <TableCell className={s.colMeta}>{t(`schemasSubmissions.common.valueType.${v.type}`)}</TableCell>
                        <TableCell className={s.colMeta}>{cadenceLabel(v.cadence, t)}</TableCell>
                        <TableCell className={s.colMeta}>{v.kind === 'Calculated' ? t('schemasSubmissions.schemaEdit.calculated') : t('schemasSubmissions.schemaEdit.userDefined')}</TableCell>
                        <TableCell className={s.colFlag}>
                          <Badge appearance="outline" color={v.enabled ? 'success' : 'subtle'}>
                            {v.enabled ? t('schemasSubmissions.common.enabled') : t('schemasSubmissions.common.disabled')}
                          </Badge>
                        </TableCell>
                        <TableCell className={s.colFlag}>{v.required ? t('schemasSubmissions.common.yes') : t('schemasSubmissions.common.no')}</TableCell>
                        {!readOnly && (
                          <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                            <RowActions
                              ariaLabel={t('schemasSubmissions.common.actionsFor', { name: display })}
                              actions={[
                                { key: 'edit', label: t('schemasSubmissions.common.edit'), icon: <Edit20Regular />, onClick: () => setEditingValueIndex(i) },
                                { key: 'delete', label: t('schemasSubmissions.common.remove'), icon: <Delete20Regular />, destructive: true, onClick: () => removeValue(i) },
                              ]}
                            />
                          </TableCell>
                        )}
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            )}
            </div>
            )}

            {tab === 'layout' && (
            <div className={s.tabPanel}>
            <Field hint={t('schemasSubmissions.schemaEdit.layoutHint')}>
              {/* `inert` makes the whole subtree non-interactive in the read-only snapshot view. */}
              <div className={readOnly ? s.readOnlyLayout : undefined} {...(readOnly ? { inert: true } : {})}>
                <LayoutTreeEditor
                  schema={previewSchema!}
                  onChange={patchLayout}
                />
              </div>
            </Field>
            </div>
            )}

            {submitError && (
              <AutoScrollMessageBar intent="error">
                <MessageBarBody>{submitError}</MessageBarBody>
              </AutoScrollMessageBar>
            )}

            {!readOnly && (
              <>
                <Divider />
                <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                  <Button onClick={() => nav('/schemas')}>{t('schemasSubmissions.common.cancel')}</Button>
                  <Button appearance="secondary" icon={<Eye20Regular />} onClick={() => setPreviewOpen(true)}>{t('schemasSubmissions.common.preview')}</Button>
                  <Button appearance="primary" disabled={isBusy} onClick={onSave}>
                    {isEdit ? t('schemasSubmissions.common.saveChanges') : t('schemasSubmissions.schemaEdit.create')}
                  </Button>
                </div>
              </>
            )}
          </div>
        </Card>
      )}

      {req && (
        <Drawer
          type="overlay"
          separator
          position="end"
          open={editingValueIndex !== null}
          onOpenChange={(_, d) => { if (!d.open) closeValueDrawer() }}
          className={s.drawer}
          style={valueDrawerExpanded ? { width: DRAWER_EXPANDED_WIDTH } : undefined}
        >
          <DrawerHeaderWithClose
            title={editingValueIndex !== null
              ? (req.values[editingValueIndex]?.label || req.values[editingValueIndex]?.name || t('schemasSubmissions.schemaEdit.valueDetails'))
              : t('schemasSubmissions.schemaEdit.valueDetails')}
            onClose={closeValueDrawer}
            expanded={valueDrawerExpanded}
            onToggleExpand={() => setValueDrawerExpanded(e => !e)}
          />
          {!readOnly && editingValueIndex !== null && (
            <Toolbar className={s.drawerToolbar}>
              <ToolbarButton icon={<Delete20Regular />} onClick={() => removeValue(editingValueIndex)}>{t('schemasSubmissions.common.remove')}</ToolbarButton>
            </Toolbar>
          )}
          <DrawerBody>
            {editingValueIndex !== null && req.values[editingValueIndex] && (
              <div className={s.drawerForm}>
                <ValueEditor
                  value={req.values[editingValueIndex]}
                  schemaVersion={req.version ?? 1}
                  nameLocked={lockedValueNames.has(req.values[editingValueIndex].name)}
                  disabled={readOnly}
                  valueNames={req.values.map(v => v.name)}
                  onChange={patch => patchValue(editingValueIndex, patch)}
                />
              </div>
            )}
          </DrawerBody>
        </Drawer>
      )}

      <Dialog open={versionDialogOpen} onOpenChange={(_, d) => setVersionDialogOpen(d.open)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>{t('schemasSubmissions.schemaEdit.publishChanges')}</DialogTitle>
            <DialogContent>
              <Body1 style={{ display: 'block', marginBottom: 12 }}>
                {t('schemasSubmissions.schemaEdit.publishHelp')}
              </Body1>
              <RadioGroup value={publishChoice} onChange={(_, d) => setPublishChoice(d.value as PublishChoice)}>
                <div className={s.dialogOptions}>
                  <Radio value="increment" label={t('schemasSubmissions.schemaEdit.incrementVersion', { version: originalVersion + 1 })} />
                  <Radio value="asis" label={t('schemasSubmissions.schemaEdit.publishAsIs')} />
                  <Radio value="draft" label={t('schemasSubmissions.schemaEdit.moveToDraft')} disabled={submissionCount > 0} />
                  {submissionCount > 0 && (
                    <span className={s.optionHint}>
                      {t('schemasSubmissions.schemaEdit.draftUnavailable', { count: submissionCount })}
                    </span>
                  )}
                  <Radio value="discard" label={t('schemasSubmissions.schemaEdit.discardChanges')} />
                </div>
              </RadioGroup>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setVersionDialogOpen(false)}>{t('schemasSubmissions.schemaEdit.cancelEditing')}</Button>
              <Button appearance="primary" disabled={isBusy} onClick={onPublishContinue}>{t('schemasSubmissions.common.continue')}</Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      {previewSchema && (
        <SchemaPreviewDialog schema={previewSchema} open={previewOpen} onClose={() => setPreviewOpen(false)} />
      )}
      {previewSchema && (
        <SchemaDependencyGraphDialog schema={previewSchema} open={dependenciesOpen} onClose={() => setDependenciesOpen(false)} />
      )}
      {schemaId && (
        <SchemaCommentsDrawer
          open={commentsOpen}
          onClose={() => setCommentsOpen(false)}
          schemaId={schemaId}
          values={req?.values ?? []}
        />
      )}
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

function ValueEditor({ value, schemaVersion, nameLocked, disabled, onChange, valueNames }: {
  value: SchemaValue
  /** Parent schema's current `version` — used to bound the "Since version" input. */
  schemaVersion: number
  /** When true the value already exists in the saved schema, so its name is read-only (renaming would break rules/submissions). */
  nameLocked: boolean
  /** When true the whole editor is read-only (snapshot view): inputs disabled. */
  disabled?: boolean
  onChange: (patch: Partial<SchemaValue>) => void
  /** All value names in the schema — fed to the expression editors' autocomplete. */
  valueNames: string[]
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const calculated = value.kind === 'Calculated'
  const kinds: SchemaValueKind[] = ['UserDefined', 'Calculated']
  // Sibling names (everything but this value) for the calculated Expression; the gating/warning
  // rules can also reference this value itself, and value validation adds value/minimum/maximum.
  const siblingNames = valueNames.filter(n => n && n !== value.name)
  const validationVars = ['value', 'minimum', 'maximum', ...siblingNames]
  return (
    <>
      <div className={s.twoCol}>
        <Field
          label={t('schemasSubmissions.common.name')}
          required
          hint={nameLocked
            ? t('schemasSubmissions.schemaEdit.valueNameFixed')
            : t('schemasSubmissions.schemaEdit.valueNameHint')}
          validationState={nameLocked || isValidValueName(value.name) ? 'none' : 'error'}
          validationMessage={nameLocked || isValidValueName(value.name) ? undefined : t('schemasSubmissions.schemaEdit.valueNameValidation')}
        >
          <Input value={value.name} disabled={nameLocked || disabled} onChange={(_, v) => onChange({ name: v.value })} />
        </Field>
        <Field label={t('schemasSubmissions.common.label')}>
          <Input value={value.label ?? ''} disabled={disabled} onChange={(_, v) => onChange({ label: v.value })} />
        </Field>
      </div>

      <Field label={t('schemasSubmissions.common.description')}>
        <Textarea value={value.description ?? ''} disabled={disabled} onChange={(_, v) => onChange({ description: v.value })} />
      </Field>

      <Field
        label={t('schemasSubmissions.schemaEdit.caption')}
        hint={t('schemasSubmissions.schemaEdit.captionHint')}
      >
        <Input value={value.caption ?? ''} disabled={disabled} onChange={(_, v) => onChange({ caption: v.value })} />
      </Field>

      <Field
        label={t('schemasSubmissions.schemaEdit.sinceVersion')}
        hint={t('schemasSubmissions.schemaEdit.sinceVersionHint', { version: schemaVersion })}
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

      <Field label={t('schemasSubmissions.schemaEdit.kind')} hint={t('schemasSubmissions.schemaEdit.kindHint')}>
        <Dropdown
          value={calculated ? t('schemasSubmissions.schemaEdit.calculated') : t('schemasSubmissions.schemaEdit.userDefined')}
          disabled={disabled}
          selectedOptions={[value.kind ?? 'UserDefined']}
          onOptionSelect={(_, d) => {
            const kind = d.optionValue as SchemaValueKind
            onChange({
              kind,
              required: kind === 'Calculated' ? false : value.required,
              expression: kind === 'Calculated' ? (value.expression ?? '') : null,
            })
          }}
        >
          {kinds.map(k => <Option key={k} value={k}>{k === 'UserDefined' ? t('schemasSubmissions.schemaEdit.userDefined') : t('schemasSubmissions.schemaEdit.calculated')}</Option>)}
        </Dropdown>
      </Field>

      {calculated && (
        <RuleTextarea
          label={t('schemasSubmissions.schemaEdit.expression')}
          hint={t('schemasSubmissions.schemaEdit.expressionHint')}
          rows={3}
          disabled={disabled}
          identifiers={siblingNames}
          value={value.expression ?? ''}
          onChange={(v) => onChange({ expression: v })}
        />
      )}

      <div className={s.twoCol}>
        <Field label={t('schemasSubmissions.common.type')}>
          <Dropdown value={t(`schemasSubmissions.common.valueType.${value.type}`)} disabled={disabled} selectedOptions={[value.type]} onOptionSelect={(_, d) => onChange({ type: d.optionValue as SchemaValueType })}>
            {types.map(type => <Option key={type} value={type}>{t(`schemasSubmissions.common.valueType.${type}`)}</Option>)}
          </Dropdown>
        </Field>
        <Field label={t('schemasSubmissions.common.cadenceLabel')}>
          <Dropdown value={cadenceLabel(value.cadence, t)} disabled={disabled} selectedOptions={[value.cadence]} onOptionSelect={(_, d) => onChange({ cadence: d.optionValue as Cadence })}>
            {cadences.map(c => <Option key={c} value={c} text={cadenceLabel(c, t)}>{cadenceLabel(c, t)}</Option>)}
          </Dropdown>
        </Field>
      </div>

      <Field label={t('schemasSubmissions.schemaEdit.unit')}>
        <Input value={value.unit ?? ''} disabled={disabled} onChange={(_, v) => onChange({ unit: v.value })} />
      </Field>

      <div className={s.flagsRow}>
        <Checkbox label={t('schemasSubmissions.sampleFields.required')} checked={value.required} disabled={disabled || calculated} onChange={(_, d) => onChange({ required: !!d.checked })} />
        <Checkbox label={t('schemasSubmissions.common.modifiable')} checked={value.modifiable} disabled={disabled || calculated} onChange={(_, d) => onChange({ modifiable: !!d.checked })} />
        <Checkbox label={t('schemasSubmissions.common.enabled')} checked={value.enabled} disabled={disabled} onChange={(_, d) => onChange({ enabled: !!d.checked })} />
      </div>

      {!calculated && (value.type === 'Integer' || value.type === 'Number') && (
        <>
          <div className={s.sectionLabel}>{t('schemasSubmissions.schemaEdit.numericConstraints')}</div>
          <div className={s.twoCol}>
            <Field label={t('schemasSubmissions.schemaEdit.min')}>
              <Input type="number" disabled={disabled} value={value.min?.toString() ?? ''} onChange={(_, v) => onChange({ min: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label={t('schemasSubmissions.schemaEdit.max')}>
              <Input type="number" disabled={disabled} value={value.max?.toString() ?? ''} onChange={(_, v) => onChange({ max: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
        </>
      )}

      {(value.type === 'Integer' || value.type === 'Number') && (
        <>
          <div className={s.sectionLabel}>{t('schemasSubmissions.schemaEdit.targetBand')}</div>
          <div className={s.bandHelp}>
            {t('schemasSubmissions.schemaEdit.targetBandHelp')}
          </div>
          <div className={s.twoCol}>
            <Field label={t('schemasSubmissions.schemaEdit.amberMin')} hint={t('schemasSubmissions.schemaEdit.amberMinHint')}>
              <Input type="number" disabled={disabled} value={value.amberMin?.toString() ?? ''} onChange={(_, v) => onChange({ amberMin: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label={t('schemasSubmissions.schemaEdit.amberMax')} hint={t('schemasSubmissions.schemaEdit.amberMaxHint')}>
              <Input type="number" disabled={disabled} value={value.amberMax?.toString() ?? ''} onChange={(_, v) => onChange({ amberMax: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label={t('schemasSubmissions.schemaEdit.greenMin')} hint={t('schemasSubmissions.schemaEdit.greenMinHint')}>
              <Input type="number" disabled={disabled} value={value.greenMin?.toString() ?? ''} onChange={(_, v) => onChange({ greenMin: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label={t('schemasSubmissions.schemaEdit.greenMax')} hint={t('schemasSubmissions.schemaEdit.greenMaxHint')}>
              <Input type="number" disabled={disabled} value={value.greenMax?.toString() ?? ''} onChange={(_, v) => onChange({ greenMax: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
        </>
      )}

      {!calculated && value.type === 'Date' && (
        <>
          <div className={s.sectionLabel}>{t('schemasSubmissions.schemaEdit.dateConstraints')}</div>
          <div className={s.twoCol}>
            <Field label={t('schemasSubmissions.schemaEdit.minDate')}>
              <Input disabled={disabled} value={value.minDate ?? ''} onChange={(_, v) => onChange({ minDate: v.value || null })} />
            </Field>
            <Field label={t('schemasSubmissions.schemaEdit.maxDate')}>
              <Input disabled={disabled} value={value.maxDate ?? ''} onChange={(_, v) => onChange({ maxDate: v.value || null })} />
            </Field>
          </div>
        </>
      )}

      {!calculated && value.type === 'String' && (
        <>
          <div className={s.sectionLabel}>{t('schemasSubmissions.schemaEdit.stringConstraints')}</div>
          <div className={s.twoCol}>
            <Field label={t('schemasSubmissions.schemaEdit.minLength')}>
              <Input type="number" disabled={disabled} value={value.minLength?.toString() ?? ''} onChange={(_, v) => onChange({ minLength: v.value === '' ? null : Number(v.value) })} />
            </Field>
            <Field label={t('schemasSubmissions.schemaEdit.maxLength')}>
              <Input type="number" disabled={disabled} value={value.maxLength?.toString() ?? ''} onChange={(_, v) => onChange({ maxLength: v.value === '' ? null : Number(v.value) })} />
            </Field>
          </div>
          <Field label={t('schemasSubmissions.schemaEdit.regexPattern')}>
            <Input disabled={disabled} value={value.regexPattern ?? ''} onChange={(_, v) => onChange({ regexPattern: v.value })} />
          </Field>
        </>
      )}

      <div className={s.sectionLabel}>{t('schemasSubmissions.schemaEdit.validation')}</div>
      {!calculated && (
        <RuleTextarea
          label={t('schemasSubmissions.schemaEdit.valueValidation')}
          hint={t('schemasSubmissions.schemaEdit.valueValidationHint')}
          rows={3}
          disabled={disabled}
          identifiers={validationVars}
          value={value.valueValidation ?? ''}
          onChange={(v) => onChange({ valueValidation: v })}
        />
      )}
      <RuleTextarea
        label={t('schemasSubmissions.schemaEdit.warning')}
        hint={t('schemasSubmissions.schemaEdit.warningHint')}
        rows={3}
        disabled={disabled}
        identifiers={validationVars}
        value={value.warning ?? ''}
        onChange={(v) => onChange({ warning: v })}
      />

      <div className={s.sectionLabel}>{t('schemasSubmissions.schemaEdit.conditionalDisplay')}</div>
      <RuleTextarea
        label={t('schemasSubmissions.schemaEdit.enabledIf')}
        hint={t('schemasSubmissions.schemaEdit.enabledIfHint')}
        rows={2}
        disabled={disabled}
        identifiers={validationVars}
        value={value.enabledIf ?? ''}
        onChange={(v) => onChange({ enabledIf: v })}
      />
      <RuleTextarea
        label={t('schemasSubmissions.schemaEdit.visibleIf')}
        hint={t('schemasSubmissions.schemaEdit.visibleIfHint')}
        rows={2}
        disabled={disabled}
        identifiers={validationVars}
        value={value.visibleIf ?? ''}
        onChange={(v) => onChange({ visibleIf: v })}
      />

      <Field label={t('schemasSubmissions.common.notes')}>
        <Textarea value={value.notes ?? ''} disabled={disabled} onChange={(_, v) => onChange({ notes: v.value })} />
      </Field>
    </>
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
  label, hint, rows, value, disabled, onChange, identifiers, placeholder,
}: {
  label: string
  hint?: string
  rows?: number
  value: string
  disabled?: boolean
  onChange: (next: string) => void
  /** Value names / context variables offered as autocomplete suggestions. */
  identifiers?: string[]
  placeholder?: string
}) {
  const s = useStyles()
  const { t } = useTranslation()
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

  // The status line lives in a fixed-height row (see `ruleStatus`) so cycling Checking… → Valid →
  // Syntax error never reflows the form. "Checking…" is shown while the server round-trips instead
  // of leaving the row blank, so there's no flash of empty space mid-edit. Read-only fields show
  // nothing (they aren't validated).
  let statusClass = s.ruleStatusChecking
  let statusContent: ReactNode = null
  if (status === 'checking') {
    statusContent = <><Spinner size="extra-tiny" /> {t('schemasSubmissions.schemaEdit.checking')}</>
  } else if (status !== 'idle') {
    if (status.ok) {
      statusClass = s.ruleStatusOk
      statusContent = <><CheckmarkCircle16Regular /> {t('schemasSubmissions.schemaEdit.validSyntax')}</>
    } else {
      statusClass = s.ruleStatusError
      statusContent = <><ErrorCircle16Regular /> {t('schemasSubmissions.schemaEdit.syntaxError')} {status.error}</>
    }
  }

  return (
    <Field label={label} hint={hint}>
      <ExpressionField
        rows={rows}
        value={value}
        disabled={disabled}
        identifiers={identifiers}
        placeholder={placeholder}
        ariaLabel={label}
        onChange={onChange}
      />
      {!disabled && <div className={mergeClasses(s.ruleStatus, statusClass)}>{statusContent}</div>}
    </Field>
  )
}
