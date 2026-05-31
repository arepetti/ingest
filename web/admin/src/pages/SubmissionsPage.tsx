import { Fragment, useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Badge, Body1, Drawer, DrawerBody, Dropdown, Field, Input,
  Menu, MenuItem, MenuList, MenuPopover, MenuTrigger, Option, SplitButton,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Title2, Tooltip, makeStyles, MessageBarBody, Toolbar, ToolbarButton, tokens,
} from '@fluentui/react-components'
import { Add20Regular, Delete20Regular, Edit20Regular, Eye20Regular, Open20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError } from '../api/client'
import { useAccounts, useDeleteSubmission, useMe, useMySchemas, useMySubmissions, useSchemas, useSubmissions } from '../api/hooks'
import { RowActions } from '../components/RowActions'
import { SubmissionAvatar } from '../components/Avatars'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { ValueLabel } from '../components/ValueLabel'
import { confirmDelete } from '../utils/confirm'
import { formatDate, formatDateTime } from '../utils/format'
import { walkLayout, type RenderItem } from '../utils/layout'
import type { Account, Schema, Submission } from '../api/types'

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
  filters: { display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  actionsHeader: { textAlign: 'right' },
  actionsCell:   { textAlign: 'right' },
  rowClickable: { cursor: 'pointer' },
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
  const { data: me } = useMe()
  const isService = me?.role === 'Service'
  const isAdmin = me?.role === 'Admin'

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [serviceId, setServiceId] = useState<string | undefined>(undefined)
  const [schemaName, setSchemaName] = useState<string | undefined>(undefined)
  const [interval, setInterval] = useState<Interval>('all')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [viewing, setViewing] = useState<Submission | null>(null)
  const [viewerExpanded, setViewerExpanded] = useState(false)

  // Recompute the from/to pair whenever the interval changes so React Query gets a stable cache key.
  const { from, to } = useMemo(
    () => intervalRange(interval, customFrom, customTo),
    [interval, customFrom, customTo],
  )

  const services = useAccounts({ role: 'Service' }, !isService)
  const adminSubs = useSubmissions({ page, pageSize, serviceId, schemaName, from, to }, !isService)
  const mySubs = useMySubmissions({ page, pageSize, schemaName, from, to }, isService)
  // Schemas are needed by the read-only view drawer (value labels + units), not by the list itself.
  // Cached by react-query so the click latency stays close to zero on subsequent opens.
  const adminSchemas = useSchemas(undefined, !isService)
  const mySchemas = useMySchemas(isService)
  const del = useDeleteSubmission()

  const submissions = isService ? mySubs : adminSubs
  const { data, isLoading, error } = submissions
  // Column count for the loading / empty placeholder rows (Service column is admin-only).
  const colSpan = isService ? 6 : 7

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

  function changeInterval(next: Interval) {
    setInterval(next)
    setPage(1)
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>{isService ? 'My submissions' : 'Submissions'}</Title2>
        <Toolbar>
          <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={() => nav('/submissions/new')}>
            New submission
          </ToolbarButton>
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

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Submitted at</TableHeaderCell>
            {!isService && <TableHeaderCell>Service</TableHeaderCell>}
            <TableHeaderCell>Schema</TableHeaderCell>
            <TableHeaderCell>Samples</TableHeaderCell>
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
              onClick={() => setViewing(sub)}
            >
              <TableCell>
                <Tooltip content={formatDateTime(sub.submittedAt)} relationship="label">
                  <TableCellLayout media={<SubmissionAvatar />}>
                    {formatDate(sub.submittedAt)}
                  </TableCellLayout>
                </Tooltip>
              </TableCell>
              {!isService && <TableCell>{resolveServiceLabel(sub, isService, me, services.data?.items ?? [])}</TableCell>}
              <TableCell>{resolveSchemaLabel(sub, schemasByName)}</TableCell>
              <TableCell>{sub.samples.length}</TableCell>
              <TableCell>
                <Tooltip content={formatDateTime(sub.createdAt)} relationship="label">
                  <span>{formatDate(sub.createdAt)}</span>
                </Tooltip>
              </TableCell>
              <TableCell>{sub.createdBy || '—'}</TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
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
                    // Only admins can hard-delete; for everyone else this would just 403 anyway.
                    ...(isAdmin ? [{ key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => { if (confirmDelete('submission', submissionLabel(sub))) del.mutate(sub.id) } }] : []),
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
            {isAdmin && (
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
            />
          )}
        </DrawerBody>
      </Drawer>
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
  submission, serviceLabel, schema,
}: {
  submission: Submission
  serviceLabel: string
  schema?: Schema
}) {
  const s = useStyles()
  // A submission has at most one schema (the editor enforces it). Use the first sample's
  // schemaName as the source of truth and prefer the schema's friendly label when loaded.
  const schemaName = submission.samples[0]?.schemaName
  const schemaDisplay = schema?.label || schemaName || '—'

  return (
    <div className={s.drawerForm}>
      <div className={s.threeCol}>
        <Field label="Service"><Body1>{serviceLabel}</Body1></Field>
        <Field label="Schema"><Body1>{schemaDisplay}</Body1></Field>
        <Field label="Samples"><Body1>{submission.samples.length}</Body1></Field>
      </div>

      <Field label="Submitted at"><Body1>{new Date(submission.submittedAt).toLocaleString()}</Body1></Field>

      <div className={s.sectionLabel}>Audit</div>
      <div className={s.twoCol}>
        <Field label="Created">
          <Body1>
            {new Date(submission.createdAt).toLocaleString()}
            {submission.createdBy ? ` · by ${submission.createdBy}` : ''}
          </Body1>
        </Field>
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
