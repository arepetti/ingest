import { Fragment, useMemo, useState, type ReactNode } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import {
  Badge, Body1, Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle, Drawer, DrawerBody, Dropdown, Field, Input,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger, Option, SplitButton,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow, Text, Textarea,
  Title2, Tooltip, makeStyles, MessageBarBody, Toolbar, ToolbarButton, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowClockwise20Regular, ArrowDownload20Regular, ArrowUpload20Regular, Checkmark20Regular, Delete20Regular, Dismiss20Regular, Edit20Regular, Eye20Regular, MoreHorizontal20Regular, Open20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { BulkImportDialog } from '../components/BulkImportDialog'
import { formatApiError } from '../api/client'
import { fetchAllMySubmissions, fetchAllSubmissions, useAccounts, useApproveSubmission, useCapabilities, useDeleteSubmission, useMySchemas, useMySubmissions, useRejectSubmission, useSchemas, useSubmissions } from '../api/hooks'
import { RowActions } from '../components/RowActions'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import { SubmissionAvatar } from '../components/Avatars'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { ValueLabel } from '../components/ValueLabel'
import { confirmDelete } from '../utils/confirm'
import { formatDate, formatDateTime } from '../utils/format'
import { walkLayout, type RenderItem } from '../utils/layout'
import { clickableRowProps } from '../utils/a11y'
import type { Account, ApprovalStatus, Schema, Submission } from '../api/types'

/** Approval-status filter values for the dropdown. 'all' clears the filter. */
type ApprovalFilter = 'all' | ApprovalStatus

const approvalFilterLabels: Record<ApprovalFilter, string> = {
  all:         'All statuses',
  Pending:     'Pending',
  Approved:    'Approved',
  Rejected:    'Rejected',
  NotRequired: 'Not required',
}

/** Map an approval status onto a Fluent badge colour. */
function approvalBadgeColor(status: ApprovalStatus): 'warning' | 'success' | 'danger' | 'informative' {
  switch (status) {
    case 'Pending':  return 'warning'
    case 'Approved': return 'success'
    case 'Rejected': return 'danger'
    default:         return 'informative'
  }
}

/** The reviewer's note left when a submission was rejected, if any (newest decision wins). */
function rejectionNote(sub: Submission): string | null {
  if (sub.approvalStatus !== 'Rejected') return null
  const last = [...(sub.approvals ?? [])].reverse().find(a => a.decision === 'Rejected')
  return last?.note?.trim() || null
}

/**
 * Small inline approval-state badge. Renders a dash for the `NotRequired` state — including legacy
 * submissions that predate the approval workflow and so carry no status at all (treated as not
 * required, since they were live the moment they landed).
 */
function ApprovalBadge({ status }: { status?: ApprovalStatus | null }) {
  if (!status || status === 'NotRequired') return <>—</>
  return <Badge appearance="tint" color={approvalBadgeColor(status)}>{status}</Badge>
}

/**
 * Human-friendly label for a submission, used in confirmation prompts. Submissions don't have
 * names; we synthesise one from the submitter + timestamp so the user sees enough to recognise
 * the row they just clicked.
 */
function submissionLabel(sub: Submission): string {
  const when = new Date(sub.submittedAt).toLocaleString()
  return sub.serviceName ? `${when} (${sub.serviceName})` : when
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', gap: '12px', justifyContent: 'space-between' },
  toolbarActions: { display: 'flex', alignItems: 'center', gap: '16px' },
  filters: { display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  actionsHeader: { textAlign: 'right' },
  actionsCell:   { textAlign: 'right' },
  // Inner row so the quick actions sit to the LEFT of the three-dots menu (not stacked above it)
  // and everything stays vertically centred within the row.
  actionsRow:    { display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: '8px' },
  // Quick approve/reject buttons sit inline, just before the three-dots menu.
  quickActions: { display: 'inline-flex', alignItems: 'center', gap: '8px' },
  // Colour-coded outlines so the two actions are instantly distinguishable.
  approveBtn: {
    borderTopColor: tokens.colorStatusSuccessBorder1,
    borderRightColor: tokens.colorStatusSuccessBorder1,
    borderBottomColor: tokens.colorStatusSuccessBorder1,
    borderLeftColor: tokens.colorStatusSuccessBorder1,
    color: tokens.colorStatusSuccessForeground1,
    ':hover': {
      borderTopColor: tokens.colorStatusSuccessBorder2,
      borderRightColor: tokens.colorStatusSuccessBorder2,
      borderBottomColor: tokens.colorStatusSuccessBorder2,
      borderLeftColor: tokens.colorStatusSuccessBorder2,
      color: tokens.colorStatusSuccessForeground1,
    },
  },
  rejectBtn: {
    borderTopColor: tokens.colorStatusDangerBorder1,
    borderRightColor: tokens.colorStatusDangerBorder1,
    borderBottomColor: tokens.colorStatusDangerBorder1,
    borderLeftColor: tokens.colorStatusDangerBorder1,
    color: tokens.colorStatusDangerForeground1,
    ':hover': {
      borderTopColor: tokens.colorStatusDangerBorder2,
      borderRightColor: tokens.colorStatusDangerBorder2,
      borderBottomColor: tokens.colorStatusDangerBorder2,
      borderLeftColor: tokens.colorStatusDangerBorder2,
      color: tokens.colorStatusDangerForeground1,
    },
  },
  rejectNote: {
    marginTop: '8px',
    padding: '8px 12px',
    borderLeft: `3px solid ${tokens.colorStatusDangerBorder1}`,
    backgroundColor: tokens.colorNeutralBackground2,
    fontSize: tokens.fontSizeBase200,
  },
  approverRow: { display: 'flex', alignItems: 'center', gap: '8px', padding: '2px 0', fontSize: tokens.fontSizeBase300 },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  drawerToolbar: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  twoCol:   { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' },
  // Top-of-drawer summary row: Service · Schema · Samples count
  threeCol: { display: 'grid', gridTemplateColumns: '2fr 2fr 1fr', gap: '12px' },
  sectionLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: 600,
    fontSize: '12px',
    textTransform: 'uppercase',
    marginTop: '12px',
  },
  valuesTable: {
    '& td, & th': { paddingTop: '6px', paddingBottom: '6px' },
  },
  // Caption "header" rows inside the values table (schema-author-provided <h2>).
  captionCell: {
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    paddingTop: '12px !important',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Section heading rows inside the values table — like caption rows but heavier weight at
  // shallow depths so nesting is visually obvious.
  sectionCell: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    paddingTop: '14px !important',
    paddingBottom: '4px !important',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  sectionDescription: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightRegular,
    fontSize: tokens.fontSizeBase200,
  },
  warningsList: {
    margin: 0,
    paddingLeft: '18px',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    color: tokens.colorNeutralForeground1,
  },
})

type Interval = 'all' | 'lastWeek' | 'lastMonth' | 'lastYear' | 'custom'

const intervalLabels: Record<Interval, string> = {
  all:       'All time',
  lastWeek:  'Last week',
  lastMonth: 'Last month',
  lastYear:  'Last year',
  custom:    'Custom range',
}

// Rolling window relative to "now". Picks UTC so the server-side filter matches the
// SubmittedAt comparisons (which are stored as UTC).
function intervalRange(interval: Interval, customFrom: string, customTo: string): { from?: string; to?: string } {
  const now = new Date()
  switch (interval) {
    case 'all':       return {}
    case 'lastWeek':  return { from: addDays(now, -7).toISOString(),  to: now.toISOString() }
    case 'lastMonth': return { from: addDays(now, -30).toISOString(), to: now.toISOString() }
    case 'lastYear':  return { from: addDays(now, -365).toISOString(), to: now.toISOString() }
    case 'custom': {
      // Both sides are optional in custom mode — leaving one empty means "open-ended".
      return {
        from: customFrom ? fromLocalInput(customFrom) : undefined,
        to:   customTo   ? fromLocalInput(customTo)   : undefined,
      }
    }
  }
}

export function SubmissionsPage() {
  const s = useStyles()
  const nav = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { me, has } = useCapabilities()
  // A caller without cross-service read only ever sees its own submissions (self-service view).
  const canReadSubmissions = has('submissions:read')
  const isService = !canReadSubmissions
  const canImport = has('submissions:submit')
  const canDelete = has('submissions:delete')
  // Approval-workflow visibility: only when the master switch is on. Acting on the queue
  // additionally needs the approve capability; the backend enforces it regardless.
  const approvalEnabled = !!me?.approvalEnabled
  const canApprove = approvalEnabled && has('submissions:approve')

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [serviceId, setServiceId] = useState<string | undefined>(undefined)
  const [schemaName, setSchemaName] = useState<string | undefined>(undefined)
  const [interval, setInterval] = useState<Interval>('all')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [viewing, setViewing] = useState<Submission | null>(null)
  const [viewerExpanded, setViewerExpanded] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState<Submission | null>(null)
  const [rejectNote, setRejectNote] = useState('')
  const [actionError, setActionError] = useState<string | null>(null)

  // The approval-status filter is mirrored in the URL so the dashboard "Review" action can deep-link
  // straight to the pending queue (?approvalStatus=Pending) and the filter survives a refresh.
  const approvalParam = searchParams.get('approvalStatus')
  const approvalFilter: ApprovalFilter =
    approvalEnabled && approvalParam && approvalParam in approvalFilterLabels
      ? (approvalParam as ApprovalFilter)
      : 'all'
  const setApprovalFilter = (next: ApprovalFilter) => {
    const sp = new URLSearchParams(searchParams)
    if (next === 'all') sp.delete('approvalStatus')
    else sp.set('approvalStatus', next)
    setSearchParams(sp, { replace: true })
    setPage(1)
  }
  const approvalStatus = approvalFilter === 'all' ? undefined : approvalFilter

  const approve = useApproveSubmission()
  const reject = useRejectSubmission()

  // Recompute the from/to pair whenever the interval changes so React Query gets a stable cache key.
  const { from, to } = useMemo(
    () => intervalRange(interval, customFrom, customTo),
    [interval, customFrom, customTo],
  )

  const services = useAccounts({ role: 'Service' }, !isService)
  const adminSubs = useSubmissions({ page, pageSize, serviceId, schemaName, from, to, approvalStatus }, !isService)
  const mySubs = useMySubmissions({ page, pageSize, schemaName, from, to }, isService)
  // Schemas are needed by the read-only view drawer (value labels + units), not by the list itself.
  // Cached by react-query so the click latency stays close to zero on subsequent opens.
  const adminSchemas = useSchemas(undefined, !isService)
  const mySchemas = useMySchemas(isService)
  const del = useDeleteSubmission()

  const submissions = isService ? mySubs : adminSubs
  const { data, isLoading, error } = submissions
  // Column count for the loading / empty placeholder rows (Service column is admin-only; the
  // approval Status column only appears when the workflow is enabled).
  const colSpan = (isService ? 7 : 8) + (approvalEnabled ? 1 : 0)

  function doApprove(sub: Submission) {
    setActionError(null)
    approve.mutate({ id: sub.id }, {
      onError: e => setActionError(formatApiError(e)),
      onSuccess: () => { if (viewing?.id === sub.id) setViewing(null) },
    })
  }

  function submitReject() {
    if (!rejecting) return
    const target = rejecting
    setActionError(null)
    reject.mutate({ id: target.id, note: rejectNote.trim() || undefined }, {
      onError: e => setActionError(formatApiError(e)),
      onSuccess: () => {
        setRejecting(null)
        setRejectNote('')
        if (viewing?.id === target.id) setViewing(null)
      },
    })
  }

  // Schemas visible to the current viewer — drives both the read-only drawer's value labels and
  // the Schema filter dropdown below.
  const schemaList = useMemo<Schema[]>(
    () => (isService ? (mySchemas.data ?? []) : (adminSchemas.data?.items ?? [])),
    [isService, adminSchemas.data, mySchemas.data],
  )
  // Pre-index schemas by name once per render — saves us doing the find() per sample in the drawer.
  const schemasByName = useMemo(
    () => new Map(schemaList.map(sc => [sc.name, sc])),
    [schemaList],
  )

  // Columns for the "Export CSV" button. The Service column is admin/operator-only, mirroring the
  // grid. Labels reuse the same resolvers the grid cells do so the file matches what's on screen.
  const exportColumns = useMemo<ExportColumn<Submission>[]>(() => {
    const cols: ExportColumn<Submission>[] = [
      { header: 'Submitted at', value: sub => sub.submittedAt },
    ]
    if (!isService) {
      cols.push({ header: 'Service', value: sub => resolveServiceLabel(sub, isService, me, services.data?.items ?? []) })
    }
    cols.push(
      { header: 'Schema', value: sub => resolveSchemaLabel(sub, schemasByName) },
      { header: 'Samples', value: sub => sub.samples.length },
      { header: 'Warnings', value: sub => sub.warnings?.length ?? 0 },
      { header: 'Created', value: sub => sub.createdAt },
      { header: 'Created by', value: sub => sub.createdBy ?? '' },
    )
    return cols
  }, [isService, me, services.data, schemasByName])

  const fetchAllForExport = () =>
    isService
      ? fetchAllMySubmissions({ schemaName, from, to })
      : fetchAllSubmissions({ serviceId, schemaName, from, to, approvalStatus })

  const submissionsExport = useCsvExport({
    filename: 'submissions.csv',
    columns: exportColumns,
    fetchAll: fetchAllForExport,
    onError: setExportError,
  })

  function changeInterval(next: Interval) {
    setInterval(next)
    setPage(1)
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>{isService ? 'My submissions' : 'Submissions'}</Title2>
        <Toolbar className={s.toolbarActions}>
          <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={() => nav('/submissions/new')}>
            New submission
          </ToolbarButton>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => submissions.refetch()}>Refresh</MenuItem>
                <MenuDivider />
                <MenuItem
                  icon={<ArrowDownload20Regular />}
                  disabled={submissionsExport.exporting}
                  onClick={submissionsExport.exportList}
                >
                  {submissionsExport.exporting ? 'Exporting…' : 'Export this list'}
                </MenuItem>
                {canImport && (
                  <MenuItem icon={<ArrowUpload20Regular />} onClick={() => setImportOpen(true)}>
                    Import bulk data
                  </MenuItem>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </Toolbar>
      </div>

      <div className={s.filters}>
        {!isService && (
          <Field label="Service">
            <Dropdown
              placeholder="All services"
              selectedOptions={serviceId ? [serviceId] : []}
              value={serviceId ? (services.data?.items.find(a => a.id === serviceId)?.label ?? services.data?.items.find(a => a.id === serviceId)?.name ?? '') : ''}
              onOptionSelect={(_, d) => { setServiceId(d.optionValue || undefined); setPage(1) }}
            >
              <Option value="">All services</Option>
              {(services.data?.items ?? []).map(a => (
                <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
              ))}
            </Dropdown>
          </Field>
        )}
        <Field label="Schema">
          <Dropdown
            placeholder="All schemas"
            selectedOptions={schemaName ? [schemaName] : []}
            value={schemaName ? (schemasByName.get(schemaName)?.label || schemaName) : ''}
            onOptionSelect={(_, d) => { setSchemaName(d.optionValue || undefined); setPage(1) }}
          >
            <Option value="">All schemas</Option>
            {schemaList.map(sc => (
              <Option key={sc.id} value={sc.name}>{sc.label || sc.name}</Option>
            ))}
          </Dropdown>
        </Field>
        {approvalEnabled && !isService && (
          <Field label="Approval">
            <Dropdown
              selectedOptions={[approvalFilter]}
              value={approvalFilterLabels[approvalFilter]}
              onOptionSelect={(_, d) => setApprovalFilter((d.optionValue as ApprovalFilter) ?? 'all')}
            >
              {(Object.keys(approvalFilterLabels) as ApprovalFilter[]).map(k => (
                <Option key={k} value={k}>{approvalFilterLabels[k]}</Option>
              ))}
            </Dropdown>
          </Field>
        )}
        <Field label="Interval">
          <Dropdown
            selectedOptions={[interval]}
            value={intervalLabels[interval]}
            onOptionSelect={(_, d) => changeInterval((d.optionValue as Interval) ?? 'all')}
          >
            {(Object.keys(intervalLabels) as Interval[]).map(k => (
              <Option key={k} value={k}>{intervalLabels[k]}</Option>
            ))}
          </Dropdown>
        </Field>
        {interval === 'custom' && (
          <>
            <Field label="From">
              <Input
                type="datetime-local"
                value={customFrom}
                onChange={(_, v) => { setCustomFrom(v.value); setPage(1) }}
              />
            </Field>
            <Field label="To">
              <Input
                type="datetime-local"
                value={customTo}
                onChange={(_, v) => { setCustomTo(v.value); setPage(1) }}
              />
            </Field>
          </>
        )}
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{formatApiError(error)}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {exportError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{exportError}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {actionError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{actionError}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Submitted at</TableHeaderCell>
            {!isService && <TableHeaderCell>Service</TableHeaderCell>}
            <TableHeaderCell>Schema</TableHeaderCell>
            {approvalEnabled && <TableHeaderCell>Status</TableHeaderCell>}
            <TableHeaderCell>Samples</TableHeaderCell>
            <TableHeaderCell>Warnings</TableHeaderCell>
            <TableHeaderCell>Created</TableHeaderCell>
            <TableHeaderCell>Created by</TableHeaderCell>
            <TableHeaderCell className={s.actionsHeader}></TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={colSpan}>Loading…</GridMessageRow>}
          {!isLoading && (data?.items ?? []).length === 0 && (
            <GridMessageRow colSpan={colSpan}>No submissions match these filters.</GridMessageRow>
          )}
          {(data?.items ?? []).map(sub => (
            <TableRow
              key={sub.id}
              className={`${s.row} ${s.rowClickable}`}
              {...clickableRowProps(() => setViewing(sub), `View submission from ${submissionLabel(sub)}`)}
            >
              <TableCell>
                <Tooltip content={formatDateTime(sub.submittedAt)} relationship="label">
                  <TableCellLayout media={<SubmissionAvatar status={sub.approvalStatus} />}>
                    {formatDate(sub.submittedAt)}
                  </TableCellLayout>
                </Tooltip>
              </TableCell>
              {!isService && <TableCell>{resolveServiceLabel(sub, isService, me, services.data?.items ?? [])}</TableCell>}
              <TableCell>{resolveSchemaLabel(sub, schemasByName)}</TableCell>
              {approvalEnabled && (
                <TableCell>
                  {sub.approvalStatus === 'Rejected' && rejectionNote(sub)
                    ? <Tooltip content={rejectionNote(sub)!} relationship="label"><span><ApprovalBadge status={sub.approvalStatus} /></span></Tooltip>
                    : <ApprovalBadge status={sub.approvalStatus} />}
                </TableCell>
              )}
              <TableCell>{sub.samples.length}</TableCell>
              <TableCell>
                {(sub.warnings?.length ?? 0) > 0
                  ? <Badge appearance="tint" color="warning">{sub.warnings.length}</Badge>
                  : '—'}
              </TableCell>
              <TableCell>
                <Tooltip content={formatDateTime(sub.createdAt)} relationship="label">
                  <span>{formatDate(sub.createdAt)}</span>
                </Tooltip>
              </TableCell>
              <TableCell>{sub.createdBy || '—'}</TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <div className={s.actionsRow}>
                  {canApprove && sub.approvalStatus === 'Pending' && (
                    <span className={s.quickActions}>
                      <Button
                        size="small"
                        appearance="outline"
                        className={s.approveBtn}
                        icon={<Checkmark20Regular />}
                        disabled={approve.isPending || reject.isPending}
                        onClick={() => doApprove(sub)}
                      >
                        Approve
                      </Button>
                      <Tooltip content="Reject" relationship="label">
                        <Button
                          size="small"
                          appearance="outline"
                          className={s.rejectBtn}
                          icon={<Dismiss20Regular />}
                          aria-label="Reject"
                          disabled={approve.isPending || reject.isPending}
                          onClick={() => { setRejectNote(''); setRejecting(sub) }}
                        />
                      </Tooltip>
                    </span>
                  )}
                  <RowActions
                    ariaLabel={`Actions for submission ${sub.id}`}
                    actions={[
                      // "View" reuses the editor layout in read-only mode (same look as Edit, just
                      // disabled). "View details" goes to the raw-table page for the flat sample
                      // dump — handy for diffing values or copy-pasting. The row click still opens
                      // the quick-look drawer beside the list.
                      { key: 'view-form',    label: 'View',         icon: <Eye20Regular />,    onClick: () => nav(`/submissions/${sub.id}/view`) },
                      { key: 'view-details', label: 'View details', icon: <Open20Regular />,   onClick: () => nav(`/submissions/${sub.id}`) },
                      { key: 'edit',         label: 'Edit',         icon: <Edit20Regular />,   onClick: () => nav(`/submissions/${sub.id}/edit`) },
                      // Hard-delete needs the submissions:delete capability; for everyone else this would just 403 anyway.
                      ...(canDelete ? [{ key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => { if (confirmDelete('submission', submissionLabel(sub))) del.mutate(sub.id) } }] : []),
                    ]}
                  />
                </div>
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
          title="Submission"
          onClose={() => { setViewing(null); setViewerExpanded(false) }}
          expanded={viewerExpanded}
          onToggleExpand={() => setViewerExpanded(e => !e)}
        />
        {viewing && (
          <Toolbar className={s.drawerToolbar}>
            {/* SplitButton: primary opens the read-only form view (same layout as edit), the
                chevron exposes the raw-table "View details" page for when the user wants the
                flat sample list instead. */}
            <Menu positioning="below-end">
              <MenuTrigger disableButtonEnhancement>
                {(triggerProps) => (
                  <SplitButton
                    menuButton={triggerProps}
                    primaryActionButton={{
                      onClick: () => { const id = viewing.id; setViewing(null); nav(`/submissions/${id}/view`) },
                    }}
                    appearance="subtle"
                    icon={<Eye20Regular />}
                  >
                    View
                  </SplitButton>
                )}
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<Eye20Regular />} onClick={() => { const id = viewing.id; setViewing(null); nav(`/submissions/${id}/view`) }}>
                    View
                  </MenuItem>
                  <MenuItem icon={<Open20Regular />} onClick={() => { const id = viewing.id; setViewing(null); nav(`/submissions/${id}`) }}>
                    View details
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
            <ToolbarButton icon={<Edit20Regular />} onClick={() => { const id = viewing.id; setViewing(null); nav(`/submissions/${id}/edit`) }}>
              Edit
            </ToolbarButton>
            {canApprove && viewing.approvalStatus === 'Pending' && (
              <>
                <ToolbarButton
                  icon={<Checkmark20Regular />}
                  disabled={approve.isPending || reject.isPending}
                  onClick={() => doApprove(viewing)}
                >
                  Approve
                </ToolbarButton>
                <ToolbarButton
                  icon={<Dismiss20Regular />}
                  disabled={approve.isPending || reject.isPending}
                  onClick={() => { setRejectNote(''); setRejecting(viewing) }}
                >
                  Reject
                </ToolbarButton>
              </>
            )}
            {canDelete && (
              <ToolbarButton
                icon={<Delete20Regular />}
                onClick={() => {
                  if (!confirmDelete('submission', submissionLabel(viewing))) return
                  const id = viewing.id
                  setViewing(null)
                  del.mutate(id)
                }}
              >
                Delete
              </ToolbarButton>
            )}
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && (
            <SubmissionViewBody
              submission={viewing}
              serviceLabel={resolveServiceLabel(viewing, isService, me, services.data?.items ?? [])}
              schema={schemasByName.get(viewing.samples[0]?.schemaName ?? '')}
              approvalEnabled={approvalEnabled}
            />
          )}
        </DrawerBody>
      </Drawer>

      {canImport && (
        <BulkImportDialog
          open={importOpen}
          onClose={() => setImportOpen(false)}
          services={services.data?.items ?? []}
        />
      )}

      <Dialog open={!!rejecting} onOpenChange={(_, d) => { if (!d.open) { setRejecting(null); setRejectNote('') } }}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Reject submission</DialogTitle>
            <DialogContent>
              <Body1>
                This submission will be marked rejected and excluded from the OData feed and Explore,
                but stays visible here. You can leave an optional reason for the submitter and other reviewers.
              </Body1>
              <Field label="Reason (optional)" style={{ marginTop: 12 }}>
                <Textarea
                  value={rejectNote}
                  onChange={(_, d) => setRejectNote(d.value)}
                  placeholder="e.g. Week 22 figures look transposed — please re-check the night-shift totals."
                  rows={3}
                />
              </Field>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => { setRejecting(null); setRejectNote('') }}>Cancel</Button>
              <Button appearance="primary" disabled={reject.isPending} onClick={submitReject}>
                {reject.isPending ? 'Rejecting…' : 'Reject'}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  )
}

/**
 * Resolve the friendly service label. Service users see their own submissions and `me` already
 * carries their label; admins/operators look it up from the services list they loaded for the
 * filter dropdown. In every case we fall back gracefully if the lookup misses.
 */
function resolveServiceLabel(
  submission: Submission,
  isService: boolean,
  me: { label?: string | null; name?: string } | undefined,
  services: Account[],
): string {
  if (isService) return me?.label || me?.name || submission.serviceName || '—'
  const acc = services.find(a => a.id === submission.serviceAccountId)
  return acc?.label || acc?.name || submission.serviceName || '—'
}

/**
 * Resolve the friendly schema label for a submission. A submission carries at most one schema
 * (the editor enforces it), so we read the first sample's schemaName and prefer the loaded
 * schema's label, falling back to the raw name and finally an em-dash.
 */
function resolveSchemaLabel(submission: Submission, schemasByName: Map<string, Schema>): string {
  const schemaName = submission.samples[0]?.schemaName
  if (!schemaName) return '—'
  return schemasByName.get(schemaName)?.label || schemaName
}

function SubmissionViewBody({
  submission, serviceLabel, schema, approvalEnabled,
}: {
  submission: Submission
  serviceLabel: string
  schema?: Schema
  approvalEnabled: boolean
}) {
  const s = useStyles()
  // A submission has at most one schema (the editor enforces it). Use the first sample's
  // schemaName as the source of truth and prefer the schema's friendly label when loaded.
  const schemaName = submission.samples[0]?.schemaName
  const schemaDisplay = schema?.label || schemaName || '—'
  const showApproval = approvalEnabled && submission.approvalStatus !== 'NotRequired'

  return (
    <div className={s.drawerForm}>
      <div className={s.threeCol}>
        <Field label="Service"><Body1>{serviceLabel}</Body1></Field>
        <Field label="Schema"><Body1>{schemaDisplay}</Body1></Field>
        <Field label="Samples"><Body1>{submission.samples.length}</Body1></Field>
      </div>

      {showApproval && (
        <>
          <div className={s.sectionLabel}>Approval</div>
          <div className={s.twoCol}>
            <Field label="Status"><div><ApprovalBadge status={submission.approvalStatus} /></div></Field>
            <Field label="Source"><Body1>{submission.source === 'Manual' ? 'Manual entry' : 'API'}</Body1></Field>
          </div>
          <ApprovalProgress submission={submission} styles={s} />
          {rejectionNote(submission) && (
            <div className={s.rejectNote}>
              <Text weight="semibold">Rejection reason</Text>
              <div>{rejectionNote(submission)}</div>
            </div>
          )}
        </>
      )}

      <div className={s.sectionLabel}>Audit</div>
      <div className={s.twoCol}>
        <Field label="Created">
          <Body1>
            {new Date(submission.createdAt).toLocaleString()}
            {submission.createdBy ? ` · by ${submission.createdBy}` : ''}
          </Body1>
        </Field>
        <Field label="Submitted at"><Body1>{new Date(submission.submittedAt).toLocaleString()}</Body1></Field>
        <Field label="Modified">
          <Body1>
            {new Date(submission.modifiedAt).toLocaleString()}
            {submission.modifiedBy ? ` · by ${submission.modifiedBy}` : ''}
          </Body1>
        </Field>
      </div>
      {submission.replacedAt && (
        <Field label="Replaced at"><Body1>{new Date(submission.replacedAt).toLocaleString()}</Body1></Field>
      )}
      {submission.isDeleted && (
        <Badge appearance="outline" color="danger">Deleted</Badge>
      )}

      {(submission.warnings?.length ?? 0) > 0 && (
        <>
          <div className={s.sectionLabel}>Warnings ({submission.warnings.length})</div>
          <AutoScrollMessageBar intent="warning">
            <MessageBarBody>
              <ul className={s.warningsList}>
                {submission.warnings.map((w, i) => <li key={i}>{w}</li>)}
              </ul>
            </MessageBarBody>
          </AutoScrollMessageBar>
        </>
      )}

      <div className={s.sectionLabel}>Values</div>
      <Table size="small" className={s.valuesTable}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Name</TableHeaderCell>
            <TableHeaderCell>Value</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {renderValueRows(submission, schema, s)}
        </TableBody>
      </Table>
    </div>
  )
}

/**
 * Walk the schema layout to produce the body rows for the read-only submission view. When a
 * schema is loaded we honour its layout — sections turn into heading rows, values that don't
 * have a corresponding sample disappear (and so do sections that end up empty as a result).
 * When the schema is not loaded we fall back to the previous flat rendering so submissions in
 * detached/cached states still display something useful.
 */
function renderValueRows(
  submission: Submission,
  schema: Schema | undefined,
  s: ReturnType<typeof useStyles>,
): ReactNode {
  const samplesByName = new Map(submission.samples.map(x => [x.valueName.toLowerCase(), x]))

  if (!schema) {
    return submission.samples.map((sample, i) => (
      <TableRow key={`raw-${i}`}>
        <TableCell>{sample.valueName}</TableCell>
        <TableCell>{formatSampleValue(sample.value, null)}</TableCell>
      </TableRow>
    ))
  }

  // `walkLayout` with a visibility predicate hides values that were not submitted, then folds
  // away sections whose every descendant was hidden — so reviewers never see an "Optional notes"
  // heading sitting above no actual content.
  const items = walkLayout(schema, { isValueVisible: name => samplesByName.has(name.toLowerCase()) })

  return items.map((item, i) => renderItem(item, i, schema, samplesByName, s))
}

function renderItem(
  item: RenderItem,
  index: number,
  schema: Schema,
  samplesByName: Map<string, Submission['samples'][number]>,
  s: ReturnType<typeof useStyles>,
): ReactNode {
  if (item.kind === 'section-start') {
    return (
      <TableRow key={`section-${index}`}>
        <TableCell className={s.sectionCell} colSpan={2}>
          <div style={{ paddingLeft: `${item.depth * 12}px` }}>
            {item.caption}
            {item.description && <div className={s.sectionDescription}>{item.description}</div>}
          </div>
        </TableCell>
      </TableRow>
    )
  }
  if (item.kind === 'section-end') return null
  // value row
  const sample = samplesByName.get(item.value.name.toLowerCase())
  const caption = item.value.caption?.trim() || ''
  return (
    <Fragment key={`value-${index}`}>
      {caption && (
        <TableRow>
          <TableCell className={s.captionCell} colSpan={2}>
            <div style={{ paddingLeft: `${item.depth * 12}px` }}>{caption}</div>
          </TableCell>
        </TableRow>
      )}
      <TableRow>
        <TableCell>
          <div style={{ paddingLeft: `${item.depth * 12}px` }}>
            <ValueLabel value={item.value} schema={schema} />
          </div>
        </TableCell>
        <TableCell>{formatSampleValue(sample?.value ?? null, item.value.unit)}</TableCell>
      </TableRow>
    </Fragment>
  )
}

/**
 * Compact approver summary for the drawer: how many required approvers have signed off, followed
 * by the recorded decisions (who, what, when, and any note). The `requiredApprovers` list is the
 * snapshot frozen when approval was triggered, so it reflects the policy as it stood then.
 */
function ApprovalProgress({
  submission, styles,
}: {
  submission: Submission
  styles: ReturnType<typeof useStyles>
}) {
  const required = (submission.requiredApprovers ?? []).filter(a => a.requirement === 'Required')
  const approvedIds = new Set((submission.approvals ?? []).filter(a => a.decision === 'Approved').map(a => a.approverAccountId))
  const approvedRequired = required.filter(a => approvedIds.has(a.accountId)).length
  const decisions = submission.approvals ?? []

  return (
    <>
      {required.length > 0 && submission.approvalStatus === 'Pending' && (
        <Body1>{approvedRequired} of {required.length} required {required.length === 1 ? 'approval' : 'approvals'} received.</Body1>
      )}
      {decisions.length > 0 && (
        <div>
          {decisions.map((d, i) => (
            <div key={i} className={styles.approverRow}>
              <Badge appearance="tint" color={d.decision === 'Approved' ? 'success' : 'danger'}>{d.decision}</Badge>
              <span>{d.approverName || d.approverAccountId}</span>
              <span style={{ color: tokens.colorNeutralForeground3 }}>· {new Date(d.decidedAt).toLocaleString()}</span>
              {d.note && <span style={{ color: tokens.colorNeutralForeground2 }}>— {d.note}</span>}
            </div>
          ))}
        </div>
      )}
    </>
  )
}

function formatSampleValue(v: unknown, unit?: string | null): string {
  if (v === null || v === undefined || v === '') return '—'
  const text = typeof v === 'boolean' ? (v ? 'true' : 'false') : String(v)
  return unit ? `${text} ${unit}` : text
}

function addDays(d: Date, days: number): Date {
  const r = new Date(d)
  r.setDate(r.getDate() + days)
  return r
}

function fromLocalInput(local: string): string {
  if (!local) return ''
  const d = new Date(local)
  return d.toISOString()
}
