import { useEffect, useMemo, useState } from 'react'
import {
  Accordion, AccordionHeader, AccordionItem, AccordionPanel,
  Badge, Body1, Button, Drawer, DrawerBody,
  Field, Input, Textarea, Dropdown, Option, Checkbox, Title2, Tooltip,
  Menu, MenuTrigger, MenuList, MenuItem, MenuPopover, SplitButton,
  makeStyles, MessageBar, MessageBarBody, MessageBarTitle,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Toolbar, ToolbarButton, Divider, tokens, Card, CardHeader,
} from '@fluentui/react-components'
import {
  Add20Regular, ArrowDownload20Regular, ArrowUpload20Regular, ChartMultiple20Regular,
  Copy20Regular, Delete20Regular, Dismiss16Regular, Edit20Regular,
} from '@fluentui/react-icons'
import { useNavigate } from 'react-router-dom'
import type {
  Account, Cadence, Schema, SchemaLayoutNode, SchemaValue, SchemaValueType, UpsertSchemaRequest,
} from '../api/types'
import {
  fetchSchemaExample, useAccounts, useCloneSchema, useCreateSchema,
  useDeleteSchema, useMe, useSchemas, useUpdateSchema,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import { RowActions } from '../components/RowActions'
import { SchemaAvatar } from '../components/Avatars'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { LayoutTreeEditor } from '../components/LayoutTreeEditor'
import { ValueLabel } from '../components/ValueLabel'
import { cadenceLabel } from '../utils/cadence'
import { confirmDelete } from '../utils/confirm'
import { downloadJson, pickJsonFile } from '../utils/download'
import { validateExpression, type ExpressionSyntaxResult } from '../utils/expression'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'

/**
 * C-style identifier rule mirrored on the server (`SchemaService.ValidateStructure`). A schema
 * value name must work as a plain NCalc identifier (so it can be referenced directly in rules)
 * and as a C# / JavaScript identifier (so it shows up cleanly across the stack). It also has to
 * stay out of the `<name>.minimum` / `<name>.maximum` bound namespace, which means no `.` etc.
 */
const VALUE_NAME_RE = /^[A-Za-z_][A-Za-z0-9_]*$/

function isValidValueName(name: string | null | undefined): boolean {
  return !!name && VALUE_NAME_RE.test(name)
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' },
  threeCol: { display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px' },
  flagsRow: { display: 'flex', flexWrap: 'wrap', gap: '16px', alignItems: 'center' },
  sectionLabel: { color: tokens.colorNeutralForeground3, fontWeight: 600, fontSize: '12px', textTransform: 'uppercase', marginTop: '12px' },
  valueCard: { padding: '12px', backgroundColor: tokens.colorNeutralBackground2, borderRadius: '6px' },
  valueHeader: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' },
  // Roomier rows so a 32px avatar in the first column doesn't visually touch the row borders.
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  // table-layout:fixed makes the column widths follow the header definitions instead of growing
  // to fit content. Without it `maxWidth: 0` on the name cell is silently ignored — auto layout
  // expands the column to fit the longest label, which overflows the row and looks like the
  // label is overlapping its neighbours.
  table: { tableLayout: 'fixed', width: '100%' },
  // The name column has no explicit width so it absorbs whatever the other columns leave behind,
  // then the inner `truncate` class ellipsises the label inside it.
  nameCell: { maxWidth: 0 },
  // Explicit widths for the small fixed-content columns. Sized for the visible text plus a bit
  // of slack so they don't crowd the avatar/label column.
  colValues:     { width: '80px' },
  colEnabled:    { width: '110px' },
  colModifiable: { width: '110px' },
  colAudience:   { width: '160px' },
  colActions:    { width: '80px' },
  truncate: {
    display: 'block',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  // Right-align the actions menu within its (fixed-width) cell.
  actionsHeader: { textAlign: 'right' },
  actionsCell:   { textAlign: 'right' },
  // Hint at row-click interactivity.
  rowClickable: { cursor: 'pointer' },
  // Drawer-level toolbar shown above the read-only body — same actions as the row menu.
  // width:100% + border-box so the bottom border spans the full drawer instead of just the
  // ToolbarButtons' intrinsic width.
  drawerToolbar: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Tighter rows than the page-level grids; this table is purely informational and lives in
  // a drawer where vertical space is at a premium.
  valuesTable: { '& td, & th': { paddingTop: '6px', paddingBottom: '6px' } },
  // One row per submission-level rule — a textarea (so the rule can be broken across lines)
  // plus a small "remove" button. A bit of breathing room between rows.
  rulesList: { display: 'flex', flexDirection: 'column', gap: '8px' },
  ruleRow: { display: 'flex', alignItems: 'flex-start', gap: '8px' },
  ruleTextarea: { flex: 1 },
  monospace: { fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace', fontSize: '13px' },
  preBlock: { whiteSpace: 'pre-wrap', margin: 0 },
})

const types: SchemaValueType[] = ['String', 'Integer', 'Number', 'Date', 'Boolean']
// Ordered from short to long so the dropdown reads as a natural progression.
const cadences: Cadence[] = ['Daily', 'Weekly', 'Fortnightly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

function emptySchema(): UpsertSchemaRequest {
  return {
    name: '',
    label: '',
    description: '',
    notes: '',
    modifiable: true,
    enabled: true,
    submissionValidations: [],
    isGlobal: true,
    serviceIds: [],
    values: [],
    layout: [],
    version: 1,
  }
}

function emptyValue(): SchemaValue {
  return {
    name: '',
    label: '',
    description: '',
    notes: '',
    caption: '',
    type: 'Number',
    unit: '',
    cadence: 'Weekly',
    required: true,
    modifiable: true,
    enabled: true,
    min: null,
    max: null,
    minDate: null,
    maxDate: null,
    minLength: null,
    maxLength: null,
    regexPattern: '',
    valueValidation: '',
    enabledIf: '',
    visibleIf: '',
    warning: '',
    sinceVersion: null,
  }
}

function toRequest(s: Schema): UpsertSchemaRequest {
  // Audit / server-managed fields aren't part of the upsert payload; strip them so the
  // server doesn't reject the body (and so the version-timestamp logic stays server-side).
  const {
    id: _id,
    createdAt: _c, createdBy: _cb,
    modifiedAt: _m, modifiedBy: _mb,
    versionModifiedAt: _vma,
    ...rest
  } = s
  return rest
}

export function SchemasPage() {
  const s = useStyles()
  const nav = useNavigate()
  const { data: me } = useMe()
  const isAdmin = me?.role === 'Admin'
  const { data, isLoading, error } = useSchemas()
  // The audience picker only cares about Service-role accounts (those who submit data); the kind
  // (User vs Application) is irrelevant here.
  const services = useAccounts({ role: 'Service' })
  const create = useCreateSchema()
  const update = useUpdateSchema()
  const del = useDeleteSchema()
  const clone = useCloneSchema()

  const [editing, setEditing] = useState<{ id?: string; req: UpsertSchemaRequest } | null>(null)
  const [viewing, setViewing] = useState<Schema | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [importError, setImportError] = useState<string | null>(null)
  // Per-drawer expanded state so the edit and view drawers can be enlarged independently.
  const [editorExpanded, setEditorExpanded] = useState(false)
  const [viewerExpanded, setViewerExpanded] = useState(false)

  const items = useMemo(() => data?.items ?? [], [data])

  function openCreate() {
    setEditing({ req: emptySchema() })
    setSubmitError(null)
  }
  function openEdit(sc: Schema) {
    const req = toRequest(sc)
    setEditing({ id: sc.id, req })
    setSubmitError(null)
  }
  function editFromView(sc: Schema) {
    setViewing(null)
    openEdit(sc)
  }
  function historyFromView(sc: Schema) {
    setViewing(null)
    nav(`/schemas/${encodeURIComponent(sc.name)}/history`)
  }
  function deleteFromView(sc: Schema) {
    if (!confirmDelete('schema', sc.label || sc.name)) return
    setViewing(null)
    del.mutate(sc.id)
  }

  function deleteFromRow(sc: Schema) {
    if (!confirmDelete('schema', sc.label || sc.name)) return
    del.mutate(sc.id)
  }

  function patchReq(patch: Partial<UpsertSchemaRequest>) {
    if (!editing) return
    setEditing({ ...editing, req: { ...editing.req, ...patch } })
  }

  function patchValue(index: number, patch: Partial<SchemaValue>) {
    if (!editing) return
    const values = editing.req.values.map((v, i) => i === index ? { ...v, ...patch } : v)
    patchReq({ values })
  }

  function addValue() {
    if (!editing) return
    patchReq({ values: [...editing.req.values, emptyValue()] })
  }

  function removeValue(index: number) {
    if (!editing) return
    patchReq({ values: editing.req.values.filter((_, i) => i !== index) })
  }

  function patchValidation(index: number, text: string) {
    if (!editing) return
    patchReq({
      submissionValidations: editing.req.submissionValidations.map((v, i) => i === index ? text : v),
    })
  }
  function addValidation() {
    if (!editing) return
    patchReq({ submissionValidations: [...editing.req.submissionValidations, ''] })
  }
  function removeValidation(index: number) {
    if (!editing) return
    patchReq({ submissionValidations: editing.req.submissionValidations.filter((_, i) => i !== index) })
  }

  async function onSave() {
    if (!editing) return
    setSubmitError(null)
    // Drop completely-blank rules but keep formatting (newlines, indentation) for the rest.
    const req: UpsertSchemaRequest = {
      ...editing.req,
      submissionValidations: editing.req.submissionValidations.filter(v => v.trim().length > 0),
    }
    try {
      if (editing.id) await update.mutateAsync({ id: editing.id, req })
      else await create.mutateAsync(req)
      setEditing(null)
      setEditorExpanded(false)
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  function patchLayout(layout: SchemaLayoutNode[]) {
    patchReq({ layout })
  }

  async function onUploadSchema() {
    setImportError(null)
    try {
      const parsed = await pickJsonFile()
      if (!parsed || typeof parsed !== 'object') throw new Error('JSON root must be an object.')
      // Fill in the required defaults so the server's UpsertSchemaRequest contract is
      // satisfied even for slimmer files (e.g. exported from an older version).
      const base = emptySchema()
      const req: UpsertSchemaRequest = { ...base, ...(parsed as Partial<UpsertSchemaRequest>) }
      // Server stamps versionModifiedAt itself — drop anything the uploader might have set.
      setEditing({ req })
    } catch (e) {
      // Cancelled picker arrives as "No file selected" — silent for that one case.
      const msg = String(e instanceof Error ? e.message : e)
      if (msg.includes('No file selected')) return
      setImportError(msg)
    }
  }

  function onDownloadSchemaJson(sc: Schema) {
    downloadJson(`${sc.name}.schema.json`, toRequest(sc))
  }

  async function onDownloadExample(sc: Schema) {
    try {
      const example = await fetchSchemaExample(sc.name)
      downloadJson(`${sc.name}.example.json`, example)
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  async function onCloneSchema(sc: Schema) {
    try {
      await clone.mutateAsync(sc.id)
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>Schemas</Title2>
        <Toolbar>
          {/* SplitButton: primary action is "New" (current flow); the chevron exposes
              "Upload JSON" for importing a previously-exported schema file. */}
          <Menu positioning="below-end">
            <MenuTrigger disableButtonEnhancement>
              {(triggerProps) => (
                <SplitButton
                  menuButton={triggerProps}
                  primaryActionButton={{ onClick: openCreate }}
                  appearance="primary"
                  icon={<Add20Regular />}
                >
                  New schema
                </SplitButton>
              )}
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<Add20Regular />} onClick={openCreate}>New schema</MenuItem>
                <MenuItem icon={<ArrowUpload20Regular />} onClick={onUploadSchema}>Upload JSON…</MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        </Toolbar>
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {importError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not import schema</MessageBarTitle>
            {importError}
            {' '}
            <Button appearance="transparent" size="small" onClick={() => setImportError(null)}>Dismiss</Button>
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Name</TableHeaderCell>
            <TableHeaderCell className={s.colValues}>Values</TableHeaderCell>
            <TableHeaderCell className={s.colEnabled}>Enabled</TableHeaderCell>
            <TableHeaderCell className={s.colModifiable}>Modifiable</TableHeaderCell>
            <TableHeaderCell className={s.colAudience}>Audience</TableHeaderCell>
            <TableHeaderCell className={`${s.colActions} ${s.actionsHeader}`}>Actions</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && (
            <TableRow><TableCell colSpan={6}>Loading...</TableCell></TableRow>
          )}
          {items.map(sc => (
            <TableRow
              key={sc.id}
              className={`${s.row} ${s.rowClickable}`}
              onClick={() => setViewing(sc)}
            >
              <TableCell className={s.nameCell}>
                <TableCellLayout media={<SchemaAvatar schema={sc} />}>
                  <Tooltip content={sc.label || sc.name} relationship="label">
                    <strong className={s.truncate}>{sc.label || sc.name}</strong>
                  </Tooltip>
                </TableCellLayout>
              </TableCell>
              <TableCell>{sc.values.length}</TableCell>
              <TableCell>
                <Badge appearance="outline" color={sc.enabled ? 'success' : 'subtle'}>
                  {sc.enabled ? 'Enabled' : 'Disabled'}
                </Badge>
              </TableCell>
              <TableCell>{sc.modifiable ? 'Yes' : 'No'}</TableCell>
              <TableCell>{sc.isGlobal ? 'Global' : `${sc.serviceIds.length} service(s)`}</TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <RowActions
                  ariaLabel={`Actions for ${sc.name}`}
                  actions={[
                    { key: 'edit', label: 'Edit', icon: <Edit20Regular />, onClick: () => openEdit(sc) },
                    { key: 'clone', label: 'Clone', icon: <Copy20Regular />, onClick: () => onCloneSchema(sc) },
                    // Backed by an Admin-only endpoint — surface the menu item only for admins so we
                    // don't lead operators down a path that ends in a 403.
                    ...(isAdmin
                      ? [{
                          key: 'history',
                          label: 'View historical data',
                          icon: <ChartMultiple20Regular />,
                          onClick: () => nav(`/schemas/${encodeURIComponent(sc.name)}/history`),
                        }]
                      : []),
                    { key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => deleteFromRow(sc) },
                  ]}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <Drawer
        type="overlay"
        separator
        open={!!editing}
        onOpenChange={(_, d) => { if (!d.open) { setEditing(null); setEditorExpanded(false) } }}
        position="end"
        className={s.drawer}
        style={editorExpanded ? { width: DRAWER_EXPANDED_WIDTH } : undefined}
      >
        <DrawerHeaderWithClose
          title={editing?.id ? 'Edit schema' : 'New schema'}
          onClose={() => { setEditing(null); setEditorExpanded(false) }}
          expanded={editorExpanded}
          onToggleExpand={() => setEditorExpanded(e => !e)}
        />
        <DrawerBody>
          {editing && (
            <div className={s.drawerForm}>
              <div className={s.twoCol}>
                <Field label="Name" required>
                  <Input value={editing.req.name} onChange={(_, v) => patchReq({ name: v.value })} />
                </Field>
                <Field label="Label">
                  <Input value={editing.req.label ?? ''} onChange={(_, v) => patchReq({ label: v.value })} />
                </Field>
              </div>

              <Field label="Description">
                <Textarea value={editing.req.description ?? ''} onChange={(_, v) => patchReq({ description: v.value })} />
              </Field>

              <div className={s.flagsRow}>
                <Checkbox label="Enabled" checked={editing.req.enabled} onChange={(_, d) => patchReq({ enabled: !!d.checked })} />
                <Checkbox label="Modifiable" checked={editing.req.modifiable} onChange={(_, d) => patchReq({ modifiable: !!d.checked })} />
              </div>

              <Field
                label="Version"
                hint="Bump when introducing new values. Cannot decrease. Each value's 'Since version' must be ≤ this."
              >
                <Input
                  type="number"
                  value={String(editing.req.version ?? 1)}
                  onChange={(_, v) => {
                    const n = v.value === '' ? 1 : Math.max(0, Math.floor(Number(v.value) || 0))
                    patchReq({ version: n })
                  }}
                />
              </Field>

              <div className={s.sectionLabel}>Audience</div>
              <Checkbox label="Global (visible to all services)" checked={editing.req.isGlobal} onChange={(_, d) => patchReq({ isGlobal: !!d.checked })} />
              {!editing.req.isGlobal && (
                <Field label="Visible to services">
                  <Dropdown
                    multiselect
                    selectedOptions={editing.req.serviceIds}
                    value={(services.data?.items ?? []).filter(a => editing.req.serviceIds.includes(a.id)).map(a => a.label || a.name).join(', ')}
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
                  {editing.req.submissionValidations.length === 0 && (
                    <Body1 style={{ color: tokens.colorNeutralForeground3, fontSize: 12 }}>
                      No rules yet. Use “Add rule” to create one.
                    </Body1>
                  )}
                  {editing.req.submissionValidations.map((rule, i) => (
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
                <Textarea value={editing.req.notes ?? ''} onChange={(_, v) => patchReq({ notes: v.value })} />
              </Field>

              <Divider />

              <div className={s.toolbar}>
                <div className={s.sectionLabel} style={{ marginTop: 0 }}>Values</div>
                <Button appearance="primary" icon={<Add20Regular />} size="small" onClick={addValue}>Add value</Button>
              </div>

              {editing.req.values.length === 0 && (
                <MessageBar intent="info">
                  <MessageBarBody>A schema needs at least one value to be useful.</MessageBarBody>
                </MessageBar>
              )}

              <Accordion multiple collapsible>
                {editing.req.values.map((v, i) => (
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
                        schemaVersion={editing.req.version ?? 1}
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
                    id: editing.id ?? '',
                    name: editing.req.name,
                    label: editing.req.label,
                    description: editing.req.description,
                    notes: editing.req.notes,
                    modifiable: editing.req.modifiable,
                    enabled: editing.req.enabled,
                    submissionValidations: editing.req.submissionValidations,
                    isGlobal: editing.req.isGlobal,
                    serviceIds: editing.req.serviceIds,
                    values: editing.req.values,
                    layout: editing.req.layout ?? [],
                    version: editing.req.version ?? 1,
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
                <Button onClick={() => setEditing(null)}>Cancel</Button>
                <Button appearance="primary" onClick={onSave}>Save</Button>
              </div>
            </div>
          )}
        </DrawerBody>
      </Drawer>

      <Drawer
        type="overlay"
        separator
        open={!!viewing}
        onOpenChange={(_, d) => { if (!d.open) { setViewing(null); setViewerExpanded(false) } }}
        position="end"
        className={s.drawer}
        style={viewerExpanded ? { width: DRAWER_EXPANDED_WIDTH } : undefined}
      >
        <DrawerHeaderWithClose
          title={viewing ? (viewing.label || viewing.name) : 'Schema'}
          onClose={() => { setViewing(null); setViewerExpanded(false) }}
          expanded={viewerExpanded}
          onToggleExpand={() => setViewerExpanded(e => !e)}
        />
        {viewing && (
          <Toolbar className={s.drawerToolbar}>
            <ToolbarButton icon={<Edit20Regular />} onClick={() => editFromView(viewing)}>Edit</ToolbarButton>
            <ToolbarButton icon={<Copy20Regular />} onClick={() => onCloneSchema(viewing)}>Clone</ToolbarButton>
            {/* Download: default is "schema as JSON"; chevron exposes "example submission". */}
            <Menu positioning="below-end">
              <MenuTrigger disableButtonEnhancement>
                {(triggerProps) => (
                  <SplitButton
                    menuButton={triggerProps}
                    primaryActionButton={{ onClick: () => onDownloadSchemaJson(viewing) }}
                    appearance="subtle"
                    icon={<ArrowDownload20Regular />}
                  >
                    Download
                  </SplitButton>
                )}
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<ArrowDownload20Regular />} onClick={() => onDownloadSchemaJson(viewing)}>
                    Schema as JSON
                  </MenuItem>
                  <MenuItem icon={<ArrowDownload20Regular />} onClick={() => onDownloadExample(viewing)}>
                    Example submission (JSON)
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
            {isAdmin && (
              <ToolbarButton icon={<ChartMultiple20Regular />} onClick={() => historyFromView(viewing)}>
                View historical data
              </ToolbarButton>
            )}
            <ToolbarButton icon={<Delete20Regular />} onClick={() => deleteFromView(viewing)}>Delete</ToolbarButton>
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && <SchemaViewBody schema={viewing} services={services.data?.items ?? []} />}
        </DrawerBody>
      </Drawer>
    </div>
  )
}

function SchemaViewBody({ schema, services }: { schema: Schema; services: Account[] }) {
  const s = useStyles()
  const audience = schema.isGlobal
    ? 'Global (visible to all services)'
    : (schema.serviceIds
        .map(id => {
          const a = services.find(x => x.id === id)
          return a?.label || a?.name || id
        })
        .join(', ') || '(no services)')

  return (
    <div className={s.drawerForm}>
      <div className={s.twoCol}>
        <Field label="Name"><Body1>{schema.name}</Body1></Field>
        <Field label="Label"><Body1>{schema.label || '—'}</Body1></Field>
      </div>
      {schema.description && <Field label="Description"><Body1>{schema.description}</Body1></Field>}
      <div className={s.flagsRow}>
        <Badge appearance="outline" color={schema.enabled ? 'success' : 'subtle'}>
          {schema.enabled ? 'Enabled' : 'Disabled'}
        </Badge>
        <Badge appearance="outline" color={schema.modifiable ? 'informative' : 'subtle'}>
          {schema.modifiable ? 'Modifiable' : 'Frozen'}
        </Badge>
      </div>
      <div className={s.twoCol}>
        <Field label="Audience"><Body1>{audience}</Body1></Field>
        <Field label="Version">
          <Body1>
            {schema.version}
            {schema.versionModifiedAt && (
              <span style={{ color: tokens.colorNeutralForeground3 }}>
                {' '}· bumped {new Date(schema.versionModifiedAt).toLocaleString()}
              </span>
            )}
          </Body1>
        </Field>
      </div>
      {schema.submissionValidations.length > 0 && (
        <div>
          <div className={s.sectionLabel}>Submission validations</div>
          <ol style={{ margin: 0, paddingLeft: '20px' }}>
            {schema.submissionValidations.map((v, i) => (
              <li key={i}>
                <pre className={`${s.monospace} ${s.preBlock}`}>{v}</pre>
              </li>
            ))}
          </ol>
        </div>
      )}
      {schema.notes && <Field label="Notes"><Body1>{schema.notes}</Body1></Field>}

      <Divider />

      <div className={s.sectionLabel}>Values ({schema.values.length})</div>
      {schema.values.length === 0 ? (
        <MessageBar intent="info"><MessageBarBody>No values defined.</MessageBarBody></MessageBar>
      ) : (
        <Table size="small" className={s.valuesTable}>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Label</TableHeaderCell>
              <TableHeaderCell>Type</TableHeaderCell>
              <TableHeaderCell>Cadence</TableHeaderCell>
            </TableRow>
          </TableHeader>
          <TableBody>
            {schema.values.map((v, i) => (
              <TableRow key={i}>
                <TableCell><ValueLabel value={v} schema={schema} /></TableCell>
                <TableCell>{v.type}</TableCell>
                <TableCell>{cadenceLabel(v.cadence)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <div className={s.sectionLabel}>Audit</div>
      <div className={s.twoCol}>
        <Field label="Created">
          <Body1>
            {new Date(schema.createdAt).toLocaleString()}
            {schema.createdBy ? ` · by ${schema.createdBy}` : ''}
          </Body1>
        </Field>
        <Field label="Modified">
          <Body1>
            {new Date(schema.modifiedAt).toLocaleString()}
            {schema.modifiedBy ? ` · by ${schema.modifiedBy}` : ''}
          </Body1>
        </Field>
      </div>
    </div>
  )
}

function ValueEditor({ value, schemaVersion, onChange, onRemove }: {
  value: SchemaValue
  /** Parent schema's current `version` — used to bound the "Since version" input. */
  schemaVersion: number
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
          hint="Used as the identifier in validation rules. Must start with a letter or underscore and contain only letters, digits, and underscores."
          validationState={isValidValueName(value.name) ? 'none' : 'error'}
          validationMessage={isValidValueName(value.name) ? undefined : 'Must be a valid identifier: letters, digits and underscores only; cannot start with a digit.'}
        >
          <Input value={value.name} onChange={(_, v) => onChange({ name: v.value })} />
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
    if (!trimmed) { setStatus('idle'); return }
    setStatus('checking')
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
