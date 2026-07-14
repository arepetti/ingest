import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  Badge, Body1, Button, Checkbox, Drawer, DrawerBody,
  Dropdown, Field, Input, Option, Textarea,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Title2, Toolbar, ToolbarButton, Tooltip,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  MessageBarBody, MessageBarTitle,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowClockwise20Regular, ArrowDownload20Regular, Delete20Regular, Edit20Regular, MoreHorizontal20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { EventKindAvatar } from '../components/Avatars'
import { DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { RowActions } from '../components/RowActions'
import { clickableRowProps } from '../utils/a11y'
import { confirmDelete } from '../utils/confirm'
import { formatApiError } from '../api/client'
import { formatDateTime } from '../utils/format'
import { toLocalInput, fromLocalInput } from '../utils/datetimeLocal'
import { formatDurationInput, parseDurationMinutes } from '../utils/duration'
import { eventKindLabel } from '../utils/eventKind'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import { fetchAllEvents, useAccounts, useCapabilities, useCreateEvent, useDeleteEvent, useEvents, useUpdateEvent } from '../api/hooks'
import type { Account, EventKind, IngestEvent, UpsertEventRequest } from '../api/types'

const EVENT_KINDS: EventKind[] = ['PointInTime', 'Interval', 'FromNowOn']

/** Human-readable summary of an event's duration for the table/view — "—" when not applicable. */
function durationSummary(ev: Pick<IngestEvent, 'kind' | 'durationMinutes'>): string {
  if (ev.kind === 'FromNowOn') return 'Ongoing'
  if (ev.kind !== 'Interval' || !ev.durationMinutes) return '—'
  const h = Math.floor(ev.durationMinutes / 60)
  const m = ev.durationMinutes % 60
  return [h ? `${h}h` : null, m ? `${m}m` : null].filter(Boolean).join(' ') || '0m'
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  toolbarActions: { display: 'flex', alignItems: 'center', gap: '16px' },
  drawer: { width: 'max(600px, 46vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  drawerToolbar: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  timestampCell: { whiteSpace: 'nowrap', width: '180px' },
  durationCell: { whiteSpace: 'nowrap', width: '110px' },
  labelCell: { maxWidth: 0 },
  descCell: { maxWidth: 0 },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  muted: { color: tokens.colorNeutralForeground3 },
  actionsHeader: { textAlign: 'right' },
  actionsCell: { textAlign: 'right' },
  sectionLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: 600,
    fontSize: '12px',
    textTransform: 'uppercase',
    marginTop: '12px',
  },
  // `alignItems: 'start'` stops a field with a taller hint (e.g. Duration's multi-line format hint)
  // from stretching its row-mate's control (e.g. the Kind dropdown) to match — without it, grid's
  // default `stretch` inflates the shorter field's control to fill the extra height instead of
  // just leaving it top-aligned.
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', alignItems: 'start' },
})

/** Working copy of an event while it's open in the edit drawer. */
interface EventDraft {
  id?: string
  timestampLocal: string
  label: string
  description: string
  kind: EventKind
  /** Duration in minutes, kept as free text while editing; only meaningful when kind is Interval. */
  durationMinutes: string
  allServices: boolean
  serviceIds: string[]
}

function toDraft(ev: IngestEvent): EventDraft {
  return {
    id: ev.id,
    timestampLocal: toLocalInput(ev.timestamp),
    label: ev.label,
    description: ev.description ?? '',
    kind: ev.kind,
    durationMinutes: ev.durationMinutes ? formatDurationInput(ev.durationMinutes) : '',
    allServices: ev.serviceIds.length === 0,
    serviceIds: ev.serviceIds,
  }
}

function emptyDraft(): EventDraft {
  return {
    timestampLocal: toLocalInput(new Date().toISOString()),
    label: '',
    description: '',
    kind: 'PointInTime',
    durationMinutes: '',
    allServices: true,
    serviceIds: [],
  }
}

/**
 * Admin-recorded events timeline: maintenance windows, incidents, deployments — anything worth
 * annotating on the shared timeline. Optionally scoped to the services it affects (empty = all).
 * Same table-with-drawer style as Accounts: a row click opens a read-only view drawer with an
 * Edit/Delete toolbar; there is no separate readonly-only page.
 */
export function EventsPage() {
  const s = useStyles()
  const [sp, setSp] = useSearchParams()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error, refetch } = useEvents({ page, pageSize })
  const { data: serviceAccounts } = useAccounts({ role: 'Service' })
  const { has } = useCapabilities()
  const canManage = has('events:manage')

  const create = useCreateEvent()
  const update = useUpdateEvent()
  const del = useDeleteEvent()

  const [editing, setEditing] = useState<EventDraft | null>(null)
  const [viewing, setViewing] = useState<IngestEvent | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [exportError, setExportError] = useState<string | null>(null)

  const services = useMemo(() => (serviceAccounts?.items ?? []).filter(a => !a.isDeleted), [serviceAccounts])
  const servicesById = useMemo(() => new Map(services.map(a => [a.id, a])), [services])

  const serviceSummary = useCallback((ev: IngestEvent): string => {
    if (ev.serviceIds.length === 0) return 'All services'
    return ev.serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || '(removed)').join(', ')
  }, [servicesById])

  // Columns for the "Export CSV" button; labels reuse the same resolvers the grid/view drawer use
  // so the file matches what's on screen.
  const exportColumns = useMemo<ExportColumn<IngestEvent>[]>(() => [
    { header: 'Label', value: ev => ev.label },
    { header: 'Timestamp', value: ev => ev.timestamp },
    { header: 'Kind', value: ev => eventKindLabel(ev.kind) },
    { header: 'Duration', value: ev => durationSummary(ev) },
    { header: 'Description', value: ev => ev.description ?? '' },
    { header: 'Affects', value: ev => serviceSummary(ev) },
    { header: 'Created', value: ev => ev.createdAt },
    { header: 'Created by', value: ev => ev.createdBy ?? '' },
  ], [serviceSummary])

  const eventsExport = useCsvExport({
    filename: 'events.csv',
    columns: exportColumns,
    fetchAll: () => fetchAllEvents(),
    onError: setExportError,
  })

  function openCreate() {
    setEditing(emptyDraft())
    setSubmitError(null)
  }

  // Deep link: /events?new=1 opens the create drawer immediately (used by the global search
  // "Add event" action). Guarded so it fires once, after capabilities are known; the param is
  // stripped afterwards so a refresh/back is clean.
  const openedFromUrl = useRef(false)
  useEffect(() => {
    if (openedFromUrl.current) return
    if (sp.get('new') === null || !canManage) return
    openedFromUrl.current = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot deep-link open, gated on async capabilities
    openCreate()
    const next = new URLSearchParams(sp)
    next.delete('new')
    setSp(next, { replace: true })
  }, [sp, canManage, setSp])
  function openEdit(ev: IngestEvent) {
    setEditing(toDraft(ev))
    setSubmitError(null)
  }
  function editFromView(ev: IngestEvent) { setViewing(null); openEdit(ev) }
  function deleteFromView(ev: IngestEvent) {
    if (!confirmDelete('event', ev.label)) return
    setViewing(null)
    del.mutate(ev.id)
  }
  function deleteFromRow(ev: IngestEvent) {
    if (!confirmDelete('event', ev.label)) return
    del.mutate(ev.id)
  }

  async function onSave() {
    if (!editing) return
    setSubmitError(null)
    const label = editing.label.trim()
    if (!label) { setSubmitError('Label is required.'); return }
    if (!editing.timestampLocal) { setSubmitError('Timestamp is required.'); return }
    if (!editing.allServices && editing.serviceIds.length === 0) {
      setSubmitError('Pick at least one affected service, or choose “All services”.'); return
    }
    const durationText = editing.durationMinutes.trim()
    if (durationText && parseDurationMinutes(durationText) === null) {
      setSubmitError('Duration format not recognised. Use minutes (45), hours:minutes (1:30), or days hours:minutes (2 03:15).')
      return
    }
    const durationMinutes = durationText ? parseDurationMinutes(durationText) : null
    if (editing.kind === 'Interval' && (!durationMinutes || durationMinutes <= 0)) {
      setSubmitError('Duration is required for interval events.'); return
    }

    const req: UpsertEventRequest = {
      timestamp: fromLocalInput(editing.timestampLocal),
      label,
      description: editing.description.trim() || null,
      kind: editing.kind,
      durationMinutes: editing.kind === 'Interval' ? durationMinutes : null,
      serviceIds: editing.allServices ? [] : editing.serviceIds,
    }
    try {
      if (editing.id) await update.mutateAsync({ id: editing.id, req })
      else await create.mutateAsync(req)
      setEditing(null)
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  const pending = create.isPending || update.isPending

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>Events</Title2>
        <Toolbar className={s.toolbarActions}>
          {canManage && <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={openCreate}>Add event</ToolbarButton>}
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
                  disabled={eventsExport.exporting}
                  onClick={eventsExport.exportList}
                >
                  {eventsExport.exporting ? 'Exporting…' : 'Export this list (CSV)'}
                </MenuItem>
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

      {exportError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{exportError}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Label</TableHeaderCell>
            <TableHeaderCell className={s.timestampCell}>Timestamp</TableHeaderCell>
            <TableHeaderCell>Description</TableHeaderCell>
            <TableHeaderCell className={s.durationCell}>Duration</TableHeaderCell>
            <TableHeaderCell>Affects</TableHeaderCell>
            <TableHeaderCell className={s.actionsHeader}>Actions</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={6}>Loading…</GridMessageRow>}
          {!isLoading && (data?.items ?? []).length === 0 && (
            <GridMessageRow colSpan={6}>No events yet{canManage ? ' — click “Add event” to create one.' : '.'}</GridMessageRow>
          )}
          {(data?.items ?? []).map(ev => (
            <TableRow
              key={ev.id}
              className={`${s.row} ${s.rowClickable}`}
              {...clickableRowProps(() => setViewing(ev), `View event ${ev.label}`)}
            >
              <TableCell className={s.labelCell}>
                <Tooltip content={eventKindLabel(ev.kind)} relationship="label">
                  <TableCellLayout media={<EventKindAvatar kind={ev.kind} />}>
                    <strong className={s.truncate}>{ev.label}</strong>
                  </TableCellLayout>
                </Tooltip>
              </TableCell>
              <TableCell className={s.timestampCell}>{formatDateTime(ev.timestamp)}</TableCell>
              <TableCell className={s.descCell}>
                {ev.description
                  ? <Tooltip content={ev.description} relationship="label"><span className={s.truncate}>{ev.description}</span></Tooltip>
                  : <span className={`${s.truncate} ${s.muted}`}>—</span>}
              </TableCell>
              <TableCell className={s.durationCell}>{durationSummary(ev)}</TableCell>
              <TableCell className={s.descCell}>
                <Tooltip content={serviceSummary(ev)} relationship="label">
                  <span className={s.truncate}>{serviceSummary(ev)}</span>
                </Tooltip>
              </TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <RowActions
                  ariaLabel={`Actions for event ${ev.label}`}
                  actions={[
                    ...(canManage ? [{ key: 'edit', label: 'Edit', icon: <Edit20Regular />, onClick: () => openEdit(ev) }] : []),
                    ...(canManage ? [{ key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => deleteFromRow(ev) }] : []),
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
        open={!!editing}
        onOpenChange={(_, d) => { if (!d.open) setEditing(null) }}
        position="end"
        className={s.drawer}
      >
        <DrawerHeaderWithClose
          title={editing?.id ? 'Edit event' : 'Add event'}
          onClose={() => setEditing(null)}
        />
        <DrawerBody>
          {editing && (
            <div className={s.drawerForm}>
              <Field label="Timestamp" required>
                <Input
                  type="datetime-local"
                  value={editing.timestampLocal}
                  onChange={(_, v) => setEditing({ ...editing, timestampLocal: v.value })}
                />
              </Field>
              <Field label="Label" required>
                <Input value={editing.label} onChange={(_, v) => setEditing({ ...editing, label: v.value })} />
              </Field>
              <Field label="Description">
                <Textarea value={editing.description} onChange={(_, v) => setEditing({ ...editing, description: v.value })} />
              </Field>
              <div className={s.twoCol}>
                <Field
                  label="Kind"
                  required
                  hint={editing.kind === 'FromNowOn' ? 'Runs from the timestamp above until further notice.' : undefined}
                >
                  <Dropdown
                    selectedOptions={[editing.kind]}
                    value={eventKindLabel(editing.kind)}
                    onOptionSelect={(_, d) => {
                      const kind = d.optionValue as EventKind
                      setEditing({ ...editing, kind, durationMinutes: kind === 'Interval' ? editing.durationMinutes : '' })
                    }}
                  >
                    {EVENT_KINDS.map(k => <Option key={k} value={k} text={eventKindLabel(k)}>{eventKindLabel(k)}</Option>)}
                  </Dropdown>
                </Field>
                <Field
                  label="Duration"
                  required={editing.kind === 'Interval'}
                  hint={editing.kind === 'Interval' ? 'Minutes (45), hours:minutes (1:30), or days hours:minutes (2 03:15).' : undefined}
                >
                  <Input
                    disabled={editing.kind !== 'Interval'}
                    value={editing.durationMinutes}
                    onChange={(_, v) => setEditing({ ...editing, durationMinutes: v.value })}
                    placeholder={editing.kind === 'Interval' ? 'e.g. 90 or 1:30' : 'Not applicable'}
                  />
                </Field>
              </div>
              <Field label="Affects">
                <Checkbox
                  label="All services"
                  checked={editing.allServices}
                  onChange={(_, d) => setEditing({ ...editing, allServices: !!d.checked })}
                />
                {!editing.allServices && (
                  <Dropdown
                    multiselect
                    placeholder="Select services"
                    selectedOptions={editing.serviceIds}
                    value={editing.serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || id).join(', ')}
                    onOptionSelect={(_, d) => setEditing({ ...editing, serviceIds: d.selectedOptions })}
                  >
                    {services.map(a => (
                      <Option key={a.id} value={a.id} text={a.label || a.name}>{a.label || a.name}</Option>
                    ))}
                  </Dropdown>
                )}
              </Field>

              {submitError && (
                <AutoScrollMessageBar intent="error">
                  <MessageBarBody>{submitError}</MessageBarBody>
                </AutoScrollMessageBar>
              )}

              <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 8 }}>
                <Button onClick={() => setEditing(null)} disabled={pending}>Cancel</Button>
                <Button appearance="primary" onClick={onSave} disabled={pending}>{pending ? 'Saving…' : 'Save'}</Button>
              </div>
            </div>
          )}
        </DrawerBody>
      </Drawer>

      <Drawer
        type="overlay"
        separator
        open={!!viewing}
        onOpenChange={(_, d) => { if (!d.open) setViewing(null) }}
        position="end"
        className={s.drawer}
      >
        <DrawerHeaderWithClose
          title={viewing ? viewing.label : 'Event'}
          onClose={() => setViewing(null)}
        />
        {viewing && (
          <Toolbar className={s.drawerToolbar}>
            {canManage && <ToolbarButton icon={<Edit20Regular />} onClick={() => editFromView(viewing)}>Edit</ToolbarButton>}
            {canManage && <ToolbarButton icon={<Delete20Regular />} onClick={() => deleteFromView(viewing)}>Delete</ToolbarButton>}
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && <EventViewBody event={viewing} services={services} />}
        </DrawerBody>
      </Drawer>
    </div>
  )
}

function EventViewBody({ event, services }: { event: IngestEvent; services: Account[] }) {
  const s = useStyles()
  const serviceIds = event.serviceIds ?? []
  const serviceNames = serviceIds.map(id => {
    const svc = services.find(a => a.id === id)
    return svc ? (svc.label || svc.name) : id
  })
  return (
    <div className={s.drawerForm}>
      <Field label="Timestamp"><Body1>{formatDateTime(event.timestamp)}</Body1></Field>
      <Field label="Label"><Body1>{event.label}</Body1></Field>
      <Field label="Description"><Body1>{event.description || '—'}</Body1></Field>

      <div className={s.twoCol}>
        <Field label="Kind">
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <EventKindAvatar kind={event.kind} size={24} />
            <Body1>{eventKindLabel(event.kind)}</Body1>
          </div>
        </Field>
        <Field label="Duration"><Body1>{durationSummary(event)}</Body1></Field>
      </div>

      <Field label="Affects">
        {serviceIds.length === 0 ? (
          <Body1>All services</Body1>
        ) : (
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
            {serviceNames.map((n, i) => (
              <Badge key={serviceIds[i]} appearance="outline">{n}</Badge>
            ))}
          </div>
        )}
      </Field>

      <div className={s.sectionLabel}>Audit</div>
      <div className={s.twoCol}>
        <Field label="Created">
          <Body1>
            {new Date(event.createdAt).toLocaleString()}
            {event.createdBy ? ` · by ${event.createdBy}` : ''}
          </Body1>
        </Field>
        <Field label="Modified">
          <Body1>
            {new Date(event.modifiedAt).toLocaleString()}
            {event.modifiedBy ? ` · by ${event.modifiedBy}` : ''}
          </Body1>
        </Field>
      </div>
    </div>
  )
}
