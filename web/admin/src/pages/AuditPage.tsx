import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import {
  Badge, Dropdown, Option, Tab, TabList, Tooltip,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Title2, MessageBarBody, MessageBarTitle,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { ArrowClockwise20Regular, ArrowDownload20Regular, MoreHorizontal20Regular, Send20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { AuditChangeAvatar, StatusAvatar } from '../components/Avatars'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { PeriodFilter } from '../components/PeriodFilter'
import { RowActions } from '../components/RowActions'
import { usePeriodFilter, type PeriodFilterState } from '../utils/usePeriodFilter'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import {
  auditExportUrl, fetchAllEmailOutbox, fetchAllWebhookDeliveries,
  useAuditLog, useCapabilities, useEmailOutbox, useDrainEmail,
  useWebhookDeliveries, useRedeliverWebhook, useDrainWebhooks,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import { downloadFromUrl } from '../utils/download'
import { formatDateTime } from '../utils/format'
import type {
  AuditChangeType, AuditTargetType, AuditLog, EmailStatus, EmailMessage,
  WebhookDelivery, WebhookDeliveryStatus,
} from '../api/types'

const CHANGE_TYPES: AuditChangeType[] = ['Create', 'Edit', 'Delete', 'Approve', 'Reject']
const TARGET_TYPES: AuditTargetType[] = ['User', 'Account', 'Schema', 'ApiKey', 'Submission', 'Report', 'SchemaHistory', 'ApprovalRule', 'Settings', 'Backup']

/** Friendly labels for target types whose raw enum name doesn't read well in the UI. */
const TARGET_TYPE_LABELS: Partial<Record<AuditTargetType, string>> = {
  ApiKey: 'API key',
  SchemaHistory: 'Schema history',
  ApprovalRule: 'Approval rule',
}
const targetTypeLabel = (t: AuditTargetType): string => TARGET_TYPE_LABELS[t] ?? t
const EMAIL_STATUSES: EmailStatus[] = ['Pending', 'Sending', 'Sent', 'Failed']
const WEBHOOK_STATUSES: WebhookDeliveryStatus[] = ['Pending', 'Sending', 'Sent', 'Failed']

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px' },
  filters: { display: 'flex', alignItems: 'flex-end', gap: '12px', flexWrap: 'wrap' },
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  fieldLabel: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  filterDropdown: { minWidth: '200px' },
  table: { tableLayout: 'fixed', width: '100%' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  colTime:   { width: '210px' },
  colChange: { width: '100px' },
  colTarget: { width: '120px' },
  colStatus: { width: '110px' },
  colAttempts: { width: '90px' },
  colActions: { width: '52px' },
  cellId:    { maxWidth: 0 },
  mono:      { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
})

const ALL = '__all__'

const EMAIL_EXPORT_COLUMNS: ExportColumn<EmailMessage>[] = [
  { header: 'Created', value: m => m.createdAt },
  { header: 'To', value: m => (m.toName ? `${m.toName} <${m.toAddress}>` : m.toAddress) },
  { header: 'Subject', value: m => m.subject },
  { header: 'Status', value: m => m.status },
  { header: 'Attempts', value: m => m.attempts },
  { header: 'Sent', value: m => m.sentAt ?? '' },
]

const WEBHOOK_EXPORT_COLUMNS: ExportColumn<WebhookDelivery>[] = [
  { header: 'Created', value: d => d.createdAt },
  { header: 'Event', value: d => d.event },
  { header: 'URL', value: d => d.url },
  { header: 'Status', value: d => d.status },
  { header: 'Attempts', value: d => d.attempts },
  { header: 'Delivered', value: d => d.deliveredAt ?? '' },
  { header: 'Last status code', value: d => d.lastStatusCode ?? '' },
  { header: 'Last error', value: d => d.lastError ?? '' },
]

type AuditTab = 'changes' | 'emails' | 'webhooks'

export function AuditPage() {
  const s = useStyles()
  const { me, has } = useCapabilities()
  const [tab, setTab] = useState<AuditTab>('changes')
  // The email/webhook tabs read the notification + webhook stores, so they need the matching read
  // capability on top of the server-side master switch.
  const emailEnabled = me?.emailEnabled === true && has('notifications:read')
  const webhooksEnabled = me?.webhooksEnabled === true && has('webhooks:read')
  const canDrainEmail = has('notifications:manage')
  const canDrainWebhooks = has('webhooks:manage')

  // Filter state is lifted out of the tabs so the single actions menu in the title row can act on
  // whichever tab is showing (export honours the active filters; the dropdowns still live in the
  // tab bodies and read/write this state through props).
  const [change, setChange] = useState<AuditChangeType | undefined>(undefined)
  const [targetType, setTargetType] = useState<AuditTargetType | undefined>(undefined)
  const [status, setStatus] = useState<EmailStatus | undefined>(undefined)
  const [webhookStatus, setWebhookStatus] = useState<WebhookDeliveryStatus | undefined>(undefined)
  const changesPeriod = usePeriodFilter()
  const emailsPeriod = usePeriodFilter()
  const webhooksPeriod = usePeriodFilter()
  const queryClient = useQueryClient()

  // Refetch the active tab's list, keeping the current filters (react-query refetches with the
  // existing query params).
  const onRefresh = () => {
    const key = tab === 'emails' ? 'email-outbox' : tab === 'webhooks' ? 'webhook-deliveries' : 'audit'
    queryClient.invalidateQueries({ queryKey: [key] })
  }

  const [pageError, setPageError] = useState<string | null>(null)
  const [changesExporting, setChangesExporting] = useState(false)
  const drain = useDrainEmail()
  const webhookDrain = useDrainWebhooks()
  const emailsExport = useCsvExport({
    filename: 'sent-emails.csv',
    columns: EMAIL_EXPORT_COLUMNS,
    fetchAll: () => fetchAllEmailOutbox({ status, from: emailsPeriod.from, to: emailsPeriod.to }),
    onError: setPageError,
  })
  const webhooksExport = useCsvExport({
    filename: 'webhook-deliveries.csv',
    columns: WEBHOOK_EXPORT_COLUMNS,
    fetchAll: () => fetchAllWebhookDeliveries({ status: webhookStatus, from: webhooksPeriod.from, to: webhooksPeriod.to }),
    onError: setPageError,
  })

  // The Changes tab exports via a server-streamed CSV (the whole log can be large), so it can't use
  // the in-memory useCsvExport hook the other grids share.
  async function onExportChanges() {
    setPageError(null)
    setChangesExporting(true)
    try {
      await downloadFromUrl(
        auditExportUrl({ change, targetType, from: changesPeriod.from, to: changesPeriod.to }),
        'audit-log.csv',
      )
    } catch (e) {
      setPageError(formatApiError(e))
    } finally {
      setChangesExporting(false)
    }
  }

  async function onDrain() {
    setPageError(null)
    try {
      await drain.mutateAsync()
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  async function onWebhookDrain() {
    setPageError(null)
    try {
      await webhookDrain.mutateAsync()
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Title2>Audit</Title2>
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem icon={<ArrowClockwise20Regular />} onClick={onRefresh}>Refresh</MenuItem>
              <MenuDivider />
              {tab === 'changes' && (
                <MenuItem
                  icon={<ArrowDownload20Regular />}
                  disabled={changesExporting}
                  onClick={onExportChanges}
                >
                  {changesExporting ? 'Exporting…' : 'Export CSV'}
                </MenuItem>
              )}
              {tab === 'emails' && emailEnabled && (
                <>
                  <MenuItem
                    icon={<ArrowDownload20Regular />}
                    disabled={emailsExport.exporting}
                    onClick={emailsExport.exportList}
                  >
                    {emailsExport.exporting ? 'Exporting…' : 'Export CSV'}
                  </MenuItem>
                  {canDrainEmail && (
                    <MenuItem icon={<Send20Regular />} disabled={drain.isPending} onClick={onDrain}>
                      {drain.isPending ? 'Sending…' : 'Send pending now'}
                    </MenuItem>
                  )}
                </>
              )}
              {tab === 'webhooks' && webhooksEnabled && (
                <>
                  <MenuItem
                    icon={<ArrowDownload20Regular />}
                    disabled={webhooksExport.exporting}
                    onClick={webhooksExport.exportList}
                  >
                    {webhooksExport.exporting ? 'Exporting…' : 'Export CSV'}
                  </MenuItem>
                  {canDrainWebhooks && (
                    <MenuItem icon={<Send20Regular />} disabled={webhookDrain.isPending} onClick={onWebhookDrain}>
                      {webhookDrain.isPending ? 'Sending…' : 'Send pending now'}
                    </MenuItem>
                  )}
                </>
              )}
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>

      <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as AuditTab)}>
        <Tab value="changes">Changes</Tab>
        {emailEnabled && <Tab value="emails">Sent emails</Tab>}
        {webhooksEnabled && <Tab value="webhooks">Webhook deliveries</Tab>}
      </TabList>

      {pageError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not complete the action</MessageBarTitle>
            {pageError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {tab === 'changes' && (
        <ChangesTab
          change={change}
          setChange={setChange}
          targetType={targetType}
          setTargetType={setTargetType}
          period={changesPeriod}
        />
      )}
      {tab === 'emails' && emailEnabled && (
        <SentEmailsTab status={status} setStatus={setStatus} period={emailsPeriod} />
      )}
      {tab === 'webhooks' && webhooksEnabled && (
        <WebhookDeliveriesTab status={webhookStatus} setStatus={setWebhookStatus} period={webhooksPeriod} setError={setPageError} />
      )}
    </div>
  )
}

function ChangesTab({
  change, setChange, targetType, setTargetType, period,
}: {
  change?: AuditChangeType
  setChange: (value: AuditChangeType | undefined) => void
  targetType?: AuditTargetType
  setTargetType: (value: AuditTargetType | undefined) => void
  period: PeriodFilterState
}) {
  const s = useStyles()

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const { data, isLoading, error } = useAuditLog({ page, pageSize, change, targetType, from: period.from, to: period.to })

  const items = data?.items ?? []

  return (
    <>
      <div className={s.filters}>
        <div className={s.field}>
          <span className={s.fieldLabel}>Change type</span>
          <Dropdown
            className={s.filterDropdown}
            selectedOptions={[change ?? ALL]}
            value={change ?? 'All'}
            onOptionSelect={(_, d) => {
              setChange(d.optionValue === ALL ? undefined : (d.optionValue as AuditChangeType))
              setPage(1)
            }}
          >
            <Option value={ALL}>All</Option>
            {CHANGE_TYPES.map(c => <Option key={c} value={c}>{c}</Option>)}
          </Dropdown>
        </div>
        <div className={s.field}>
          <span className={s.fieldLabel}>Target type</span>
          <Dropdown
            className={s.filterDropdown}
            selectedOptions={[targetType ?? ALL]}
            value={targetType ? targetTypeLabel(targetType) : 'All'}
            onOptionSelect={(_, d) => {
              setTargetType(d.optionValue === ALL ? undefined : (d.optionValue as AuditTargetType))
              setPage(1)
            }}
          >
            <Option value={ALL}>All</Option>
            {TARGET_TYPES.map(t => <Option key={t} value={t}>{targetTypeLabel(t)}</Option>)}
          </Dropdown>
        </div>
        <PeriodFilter state={period} onChange={() => setPage(1)} />
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell className={s.colTime}>Timestamp</TableHeaderCell>
            <TableHeaderCell className={s.colChange}>Change</TableHeaderCell>
            <TableHeaderCell className={s.colTarget}>Target type</TableHeaderCell>
            <TableHeaderCell>Target</TableHeaderCell>
            <TableHeaderCell>Changed by</TableHeaderCell>
            <TableHeaderCell>Note</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={6}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={6}>No changes recorded.</GridMessageRow>
          )}
          {items.map(entry => (
            <TableRow key={entry.id} className={s.row}>
              <TableCell className={s.colTime}>
                <TableCellLayout media={<AuditChangeAvatar change={entry.change} targetType={entry.targetType} />}>
                  <span className={s.truncate}>{formatDateTime(entry.timestamp)}</span>
                </TableCellLayout>
              </TableCell>
              <TableCell className={s.colChange}>
                <ChangeBadge change={entry.change} />
              </TableCell>
              <TableCell className={s.colTarget}>{targetTypeLabel(entry.targetType)}</TableCell>
              <TableCell className={s.cellId}><IdentityCell name={entry.targetName} id={entry.targetId} /></TableCell>
              <TableCell className={s.cellId}><IdentityCell name={entry.actorName} id={entry.actorId} /></TableCell>
              <TableCell className={s.cellId}>
                {entry.note
                  ? <Tooltip content={entry.note} relationship="label"><span className={s.truncate}>{entry.note}</span></Tooltip>
                  : '—'}
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
    </>
  )
}

function SentEmailsTab({
  status, setStatus, period,
}: {
  status?: EmailStatus
  setStatus: (value: EmailStatus | undefined) => void
  period: PeriodFilterState
}) {
  const s = useStyles()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const { data, isLoading, error } = useEmailOutbox({ page, pageSize, status, from: period.from, to: period.to })

  const items = data?.items ?? []

  return (
    <>
      <div className={s.filters}>
        <div className={s.field}>
          <span className={s.fieldLabel}>Status</span>
          <Dropdown
            className={s.filterDropdown}
            selectedOptions={[status ?? ALL]}
            value={status ?? 'All'}
            onOptionSelect={(_, d) => {
              setStatus(d.optionValue === ALL ? undefined : (d.optionValue as EmailStatus))
              setPage(1)
            }}
          >
            <Option value={ALL}>All</Option>
            {EMAIL_STATUSES.map(st => <Option key={st} value={st}>{st}</Option>)}
          </Dropdown>
        </div>
        <PeriodFilter state={period} onChange={() => setPage(1)} />
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell className={s.colTime}>Created</TableHeaderCell>
            <TableHeaderCell>To</TableHeaderCell>
            <TableHeaderCell>Subject</TableHeaderCell>
            <TableHeaderCell className={s.colStatus}>Status</TableHeaderCell>
            <TableHeaderCell className={s.colAttempts}>Attempts</TableHeaderCell>
            <TableHeaderCell className={s.colTime}>Sent</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={6}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={6}>No emails recorded.</GridMessageRow>
          )}
          {items.map(m => (
            <TableRow key={m.id} className={s.row}>
              <TableCell className={s.colTime}>
                <TableCellLayout media={<StatusAvatar status={m.status} name={m.toName || m.toAddress} label="Email" />}>
                  <span className={s.truncate}>{formatDateTime(m.createdAt)}</span>
                </TableCellLayout>
              </TableCell>
              <TableCell className={s.cellId}>
                <Tooltip content={m.toAddress} relationship="label">
                  <span className={s.truncate}>{m.toName || m.toAddress}</span>
                </Tooltip>
              </TableCell>
              <TableCell className={s.cellId}><span className={s.truncate}>{m.subject}</span></TableCell>
              <TableCell className={s.colStatus}><EmailStatusBadge message={m} /></TableCell>
              <TableCell className={s.colAttempts}>{m.attempts}</TableCell>
              <TableCell className={s.colTime}>
                <span className={s.truncate}>{m.sentAt ? formatDateTime(m.sentAt) : '—'}</span>
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
    </>
  )
}

function WebhookDeliveriesTab({
  status, setStatus, period, setError,
}: {
  status?: WebhookDeliveryStatus
  setStatus: (value: WebhookDeliveryStatus | undefined) => void
  period: PeriodFilterState
  setError: (message: string | null) => void
}) {
  const s = useStyles()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const { data, isLoading, error } = useWebhookDeliveries({ page, pageSize, status, from: period.from, to: period.to })
  const redeliver = useRedeliverWebhook()

  const items = data?.items ?? []

  async function onRedeliver(id: string) {
    setError(null)
    try {
      await redeliver.mutateAsync(id)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <>
      <div className={s.filters}>
        <div className={s.field}>
          <span className={s.fieldLabel}>Status</span>
          <Dropdown
            className={s.filterDropdown}
            selectedOptions={[status ?? ALL]}
            value={status ?? 'All'}
            onOptionSelect={(_, d) => {
              setStatus(d.optionValue === ALL ? undefined : (d.optionValue as WebhookDeliveryStatus))
              setPage(1)
            }}
          >
            <Option value={ALL}>All</Option>
            {WEBHOOK_STATUSES.map(st => <Option key={st} value={st}>{st}</Option>)}
          </Dropdown>
        </div>
        <PeriodFilter state={period} onChange={() => setPage(1)} />
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell className={s.colTime}>Created</TableHeaderCell>
            <TableHeaderCell>Event</TableHeaderCell>
            <TableHeaderCell>URL</TableHeaderCell>
            <TableHeaderCell className={s.colStatus}>Status</TableHeaderCell>
            <TableHeaderCell className={s.colAttempts}>Attempts</TableHeaderCell>
            <TableHeaderCell className={s.colTime}>Delivered</TableHeaderCell>
            <TableHeaderCell className={s.colActions} aria-label="Actions" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={7}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={7}>No deliveries recorded.</GridMessageRow>
          )}
          {items.map(d => (
            <TableRow key={d.id} className={s.row}>
              <TableCell className={s.colTime}>
                <TableCellLayout media={<StatusAvatar status={d.status} name={d.event} label="Delivery" />}>
                  <span className={s.truncate}>{formatDateTime(d.createdAt)}</span>
                </TableCellLayout>
              </TableCell>
              <TableCell className={s.cellId}><span className={`${s.truncate} ${s.mono}`}>{d.event}</span></TableCell>
              <TableCell className={s.cellId}>
                <Tooltip content={d.url} relationship="label">
                  <span className={`${s.truncate} ${s.mono}`}>{d.url}</span>
                </Tooltip>
              </TableCell>
              <TableCell className={s.colStatus}><WebhookStatusBadge delivery={d} /></TableCell>
              <TableCell className={s.colAttempts}>{d.attempts}</TableCell>
              <TableCell className={s.colTime}>
                <span className={s.truncate}>{d.deliveredAt ? formatDateTime(d.deliveredAt) : '—'}</span>
              </TableCell>
              <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                <RowActions
                  ariaLabel={`Actions for delivery ${d.id}`}
                  actions={[
                    {
                      key: 'redeliver', label: 'Redeliver', icon: <Send20Regular />,
                      disabled: redeliver.isPending || d.status === 'Sending',
                      onClick: () => onRedeliver(d.id),
                    },
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
    </>
  )
}

function WebhookStatusBadge({ delivery }: { delivery: WebhookDelivery }) {
  const color = delivery.status === 'Sent' ? 'success'
    : delivery.status === 'Failed' ? 'danger'
    : delivery.status === 'Sending' ? 'brand'
    : 'warning'
  const badge = <Badge appearance="outline" color={color}>{delivery.status}</Badge>
  if (delivery.lastError) {
    const detail = delivery.lastStatusCode ? `HTTP ${delivery.lastStatusCode}: ${delivery.lastError}` : delivery.lastError
    return <Tooltip content={detail} relationship="label">{badge}</Tooltip>
  }
  return badge
}

function EmailStatusBadge({ message }: { message: EmailMessage }) {
  const color = message.status === 'Sent' ? 'success'
    : message.status === 'Failed' ? 'danger'
    : message.status === 'Sending' ? 'brand'
    : 'warning'
  const badge = <Badge appearance="outline" color={color}>{message.status}</Badge>
  if (message.status === 'Failed' && message.lastError) {
    return <Tooltip content={message.lastError} relationship="label">{badge}</Tooltip>
  }
  return badge
}

function ChangeBadge({ change }: { change: AuditLog['change'] }) {
  const color =
    change === 'Create' || change === 'Approve' ? 'success'
    : change === 'Delete' || change === 'Reject' ? 'danger'
    : 'brand'
  return <Badge appearance="outline" color={color}>{change}</Badge>
}

/**
 * Render the friendly name when we have one, otherwise the raw id. The id is always available on
 * hover so an operator can copy it to locate the object even when the name is missing.
 */
function IdentityCell({ name, id }: { name?: string | null; id?: string | null }) {
  const styles = useStyles()
  if (!name && !id) return <span>—</span>
  const primary = name || id!
  return (
    <Tooltip content={id ?? primary} relationship="label">
      <span className={`${styles.truncate} ${name ? '' : styles.mono}`}>{primary}</span>
    </Tooltip>
  )
}
