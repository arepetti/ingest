import { useMemo, useState } from 'react'
import {
  Badge, Body1, Button, Drawer, DrawerBody,
  Field, Title2, Tooltip,
  Menu, MenuButton, MenuTrigger, MenuList, MenuItem, MenuDivider, MenuPopover, SplitButton,
  makeStyles, MessageBar, MessageBarBody, MessageBarTitle,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Toolbar, ToolbarButton, Divider, tokens,
} from '@fluentui/react-components'
import {
  Add20Regular, ArrowClockwise20Regular, ArrowDownload20Regular, ArrowUpload20Regular, ChartMultiple20Regular,
  CloudCheckmark20Regular, Copy20Regular, Delete20Regular, DocumentPdf20Regular, Edit20Regular, History20Regular, MoreHorizontal20Regular,
  ShieldCheckmark16Regular,
} from '@fluentui/react-icons'
import { useNavigate } from 'react-router-dom'
import type { Account, Schema, UpsertSchemaRequest } from '../api/types'
import {
  fetchAllSchemas, fetchSchemaExample, schemaPdfExportUrl, useAccounts, useCloneSchema,
  useCapabilities, useDeleteSchema, useSchemas,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import { RowActions } from '../components/RowActions'
import { SchemaPreviewDialog } from '../components/SchemaPreviewDialog'
import { SchemaAvatar } from '../components/Avatars'
import { schemaRequiresApproval } from '../utils/approvers'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import { ValueLabel } from '../components/ValueLabel'
import { ExpressionField } from '../components/ExpressionField'
import { cadenceLabel } from '../utils/cadence'
import { confirmDelete } from '../utils/confirm'
import { formatDate, formatDateTime } from '../utils/format'
import { clickableRowProps } from '../utils/a11y'
import { downloadFromUrl, downloadJson, pickJsonFile } from '../utils/download'
import { emptySchema, toRequest } from '../utils/schema'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  toolbarActions: { display: 'flex', alignItems: 'center', gap: '16px' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', alignItems: 'start' },
  threeCol: { display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px', alignItems: 'start' },
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
  colCreated:    { width: '110px' },
  colCreatedBy:  { width: '140px' },
  colActions:    { width: '80px' },
  truncate: {
    display: 'block',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  // Name + the optional "requires approval" marker, kept on one line; the label truncates while
  // the marker icon stays pinned and visible.
  nameWithMarker: { display: 'flex', alignItems: 'center', gap: '6px', minWidth: 0 },
  approvalMarker: { flexShrink: 0, color: tokens.colorBrandForeground1 },
  // Right-align the actions menu within its (fixed-width) cell.
  actionsHeader: { textAlign: 'right' },
  actionsCell:   { textAlign: 'right' },
  // Hint at row-click interactivity.
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
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
  rulesOl: { margin: 0, paddingLeft: '20px', display: 'flex', flexDirection: 'column', gap: '8px' },
})

const SCHEMA_EXPORT_COLUMNS: ExportColumn<Schema>[] = [
  { header: 'Name', value: sc => sc.name },
  { header: 'Label', value: sc => sc.label ?? '' },
  { header: 'Values', value: sc => sc.values.length },
  { header: 'Enabled', value: sc => (sc.enabled ? 'Enabled' : 'Disabled') },
  { header: 'Modifiable', value: sc => (sc.modifiable ? 'Yes' : 'No') },
  { header: 'Audience', value: sc => (sc.isGlobal ? 'Global' : `${sc.serviceIds.length} service(s)`) },
  { header: 'Created', value: sc => sc.createdAt },
  { header: 'Created by', value: sc => sc.createdBy ?? '' },
]

export function SchemasPage() {
  const s = useStyles()
  const nav = useNavigate()
  const { me, has } = useCapabilities()
  const canManage = has('schemas:manage')
  // "Test submission" runs a dry-run validation on behalf of a service, which the admin API gates
  // behind submissions:submit (no data is written).
  const canTest = has('submissions:submit')
  const approvalEnabled = !!me?.approvalEnabled
  const globalDefaultRequired = !!me?.approvalDefaultRequired
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error, refetch } = useSchemas({ page, pageSize })
  // The audience picker only cares about Service-role accounts (those who submit data); the kind
  // (User vs Application) is irrelevant here.
  const services = useAccounts({ role: 'Service' })
  const del = useDeleteSchema()
  const clone = useCloneSchema()

  const [viewing, setViewing] = useState<Schema | null>(null)
  const [testing, setTesting] = useState<Schema | null>(null)
  const [importError, setImportError] = useState<string | null>(null)
  // Surfaces failures from the row/drawer actions that no longer have an inline editor to host
  // their errors (clone, "download example").
  const [actionError, setActionError] = useState<string | null>(null)
  const [viewerExpanded, setViewerExpanded] = useState(false)
  const schemasExport = useCsvExport({
    filename: 'schemas.csv',
    columns: SCHEMA_EXPORT_COLUMNS,
    fetchAll: () => fetchAllSchemas(),
    onError: setActionError,
  })

  const items = useMemo(() => data?.items ?? [], [data])

  // Create/edit now live on their own page (see SchemaEditPage); the listing just routes there.
  function openCreate() {
    nav('/schemas/new')
  }
  function openEdit(sc: Schema) {
    nav(`/schemas/${encodeURIComponent(sc.name)}/edit`)
  }
  function editFromView(sc: Schema) {
    setViewing(null)
    openEdit(sc)
  }
  function historyFromView(sc: Schema) {
    setViewing(null)
    nav(`/schemas/${encodeURIComponent(sc.name)}/history`)
  }
  function versionsFromView(sc: Schema) {
    setViewing(null)
    nav(`/schemas/${encodeURIComponent(sc.name)}/versions`)
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

  async function onUploadSchema() {
    setImportError(null)
    try {
      const parsed = await pickJsonFile()
      if (!parsed || typeof parsed !== 'object') throw new Error('JSON root must be an object.')
      // Fill in the required defaults so the server's UpsertSchemaRequest contract is
      // satisfied even for slimmer files (e.g. exported from an older version), then hand the
      // prefilled payload to the editor page via router state.
      const base = emptySchema()
      const req: UpsertSchemaRequest = { ...base, ...(parsed as Partial<UpsertSchemaRequest>) }
      nav('/schemas/new', { state: { initialSchema: req } })
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
      setActionError(formatApiError(e))
    }
  }

  async function onExportSchemaPdf(sc: Schema) {
    try {
      await downloadFromUrl(schemaPdfExportUrl(sc.name), `${sc.name}.pdf`)
    } catch (e) {
      setActionError(formatApiError(e))
    }
  }

  async function onCloneSchema(sc: Schema) {
    try {
      await clone.mutateAsync(sc.id)
    } catch (e) {
      setActionError(formatApiError(e))
    }
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>Schemas</Title2>
        <Toolbar className={s.toolbarActions}>
          {canManage && <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={openCreate}>New schema</ToolbarButton>}
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>Refresh</MenuItem>
                <MenuDivider />
                <MenuItem
                  icon={<ArrowDownload20Regular />}
                  disabled={schemasExport.exporting}
                  onClick={schemasExport.exportList}
                >
                  {schemasExport.exporting ? 'Exporting…' : 'Export this list'}
                </MenuItem>
                <MenuItem icon={<ArrowUpload20Regular />} onClick={onUploadSchema}>Upload schema</MenuItem>
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

      {actionError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            {actionError}
            {' '}
            <Button appearance="transparent" size="small" onClick={() => setActionError(null)}>Dismiss</Button>
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
            <TableHeaderCell className={s.colCreated}>Created</TableHeaderCell>
            <TableHeaderCell className={s.colCreatedBy}>Created by</TableHeaderCell>
            <TableHeaderCell className={`${s.colActions} ${s.actionsHeader}`}>Actions</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={8}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={8}>No schemas yet — click “New schema” to create one.</GridMessageRow>
          )}
          {items.map(sc => (
            <TableRow
              key={sc.id}
              className={`${s.row} ${s.rowClickable}`}
              {...clickableRowProps(() => setViewing(sc), `View schema ${sc.label || sc.name}`)}
            >
              <TableCell className={s.nameCell}>
                <TableCellLayout media={<SchemaAvatar schema={sc} />}>
                  <span className={s.nameWithMarker}>
                    <Tooltip content={sc.label || sc.name} relationship="label">
                      <strong className={s.truncate} style={{ flex: 1, minWidth: 0 }}>{sc.label || sc.name}</strong>
                    </Tooltip>
                    {schemaRequiresApproval(sc, { approvalEnabled, globalDefaultRequired }) && (
                      <Tooltip content="Requires approval" relationship="label">
                        <ShieldCheckmark16Regular className={s.approvalMarker} aria-label="Requires approval" />
                      </Tooltip>
                    )}
                  </span>
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
              <TableCell className={s.colCreated}>
                <Tooltip content={formatDateTime(sc.createdAt)} relationship="label">
                  <span className={s.truncate}>{formatDate(sc.createdAt)}</span>
                </Tooltip>
              </TableCell>
              <TableCell className={s.colCreatedBy}>
                <Tooltip content={sc.createdBy || '—'} relationship="label">
                  <span className={s.truncate}>{sc.createdBy || '—'}</span>
                </Tooltip>
              </TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <RowActions
                  ariaLabel={`Actions for ${sc.name}`}
                  actions={[
                    // Editing/cloning/deleting are gated by schemas:manage; for read-only viewers we
                    // still surface the (read) history views so the menu stays useful.
                    ...(canManage ? [
                      { key: 'edit', label: 'Edit', icon: <Edit20Regular />, onClick: () => openEdit(sc) },
                      { key: 'clone', label: 'Clone', icon: <Copy20Regular />, onClick: () => onCloneSchema(sc) },
                    ] : []),
                    ...(canTest ? [
                      { key: 'test', label: 'Test submission', icon: <CloudCheckmark20Regular />, onClick: () => setTesting(sc) },
                    ] : []),
                    {
                      key: 'history',
                      label: 'View historical data',
                      icon: <ChartMultiple20Regular />,
                      onClick: () => nav(`/schemas/${encodeURIComponent(sc.name)}/history`),
                    }, {
                      key: 'versions',
                      label: 'View version history',
                      icon: <History20Regular />,
                      onClick: () => nav(`/schemas/${encodeURIComponent(sc.name)}/versions`),
                    },
                    ...(canManage ? [{ key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => deleteFromRow(sc) }] : []),
                  ]}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <GridPager
        page={page}
        pageSize={pageSize}
        total={data?.total ?? 0}
        onPageChange={setPage}
        onPageSizeChange={(n) => { setPageSize(n); setPage(1) }}
      />

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
            {canManage && <ToolbarButton icon={<Edit20Regular />} onClick={() => editFromView(viewing)}>Edit</ToolbarButton>}
            {canManage && <ToolbarButton icon={<Copy20Regular />} onClick={() => onCloneSchema(viewing)}>Clone</ToolbarButton>}
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
                  <MenuItem icon={<DocumentPdf20Regular />} onClick={() => onExportSchemaPdf(viewing)}>
                    Schema as PDF
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
            {/* History views are read-only, so they stay available to anyone who can view schemas.
                Default action is "view historical data"; the chevron exposes "view version history". */}
            <Menu positioning="below-end">
              <MenuTrigger disableButtonEnhancement>
                {(triggerProps) => (
                  <SplitButton
                    menuButton={triggerProps}
                    primaryActionButton={{ onClick: () => historyFromView(viewing) }}
                    appearance="subtle"
                    icon={<ChartMultiple20Regular />}
                  >
                    View historical data
                  </SplitButton>
                )}
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<ChartMultiple20Regular />} onClick={() => historyFromView(viewing)}>
                    View historical data
                  </MenuItem>
                  <MenuItem icon={<History20Regular />} onClick={() => versionsFromView(viewing)}>
                    View version history
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
            {canManage && <ToolbarButton icon={<Delete20Regular />} onClick={() => deleteFromView(viewing)}>Delete</ToolbarButton>}
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && (
            <SchemaViewBody
              schema={viewing}
              services={services.data?.items ?? []}
              requiresApproval={schemaRequiresApproval(viewing, { approvalEnabled, globalDefaultRequired })}
            />
          )}
        </DrawerBody>
      </Drawer>

      {testing && (
        <SchemaPreviewDialog
          schema={testing}
          open={!!testing}
          mode="test"
          onClose={() => setTesting(null)}
        />
      )}
    </div>
  )
}

function SchemaViewBody({ schema, services, requiresApproval }: { schema: Schema; services: Account[]; requiresApproval: boolean }) {
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
        {requiresApproval && (
          <Badge appearance="outline" color="brand" icon={<ShieldCheckmark16Regular />}>
            Requires approval
          </Badge>
        )}
      </div>
      {requiresApproval && schema.modifiable && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Modifiable and approval-gated</MessageBarTitle>
            Re-submitting data for a window that already has a submission replaces it and resets
            approval to Pending — even if it was previously approved — so the earlier values are
            withdrawn from live reporting and ultimately overwritten. Mark the schema (or value) as
            not modifiable if approved figures should never change mid-cycle.
          </MessageBarBody>
        </MessageBar>
      )}
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
          <ol className={s.rulesOl}>
            {schema.submissionValidations.map((v, i) => (
              <li key={i}>
                <ExpressionField value={v} disabled rows={1} onChange={() => {}} ariaLabel={`Submission validation rule ${i + 1}`} />
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

