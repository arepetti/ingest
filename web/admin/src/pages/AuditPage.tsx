import { useState } from 'react'
import {
  Badge, Button, Dropdown, Option, Tab, TabList, Tooltip,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Title2, Toolbar, MessageBarBody, MessageBarTitle,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { ArrowDownload20Regular, Send20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { auditExportUrl, useAuditLog, useMe, useEmailOutbox, useDrainEmail } from '../api/hooks'
import { formatApiError } from '../api/client'
import { downloadFromUrl } from '../utils/download'
import { formatDateTime } from '../utils/format'
import type { AuditChangeType, AuditTargetType, AuditLog, EmailStatus, EmailMessage } from '../api/types'

const CHANGE_TYPES: AuditChangeType[] = ['Create', 'Edit', 'Delete']
const TARGET_TYPES: AuditTargetType[] = ['User', 'Account', 'Schema', 'ApiKey', 'Submission', 'Report']
const EMAIL_STATUSES: EmailStatus[] = ['Pending', 'Sending', 'Sent', 'Failed']

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: '12px', flexWrap: 'wrap' },
  filters: { display: 'flex', alignItems: 'flex-end', gap: '12px', flexWrap: 'wrap' },
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  fieldLabel: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  filterDropdown: { minWidth: '160px' },
  table: { tableLayout: 'fixed', width: '100%' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  colTime:   { width: '180px' },
  colChange: { width: '100px' },
  colTarget: { width: '120px' },
  colStatus: { width: '110px' },
  colAttempts: { width: '90px' },
  cellId:    { maxWidth: 0 },
  mono:      { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
})

const ALL = '__all__'

type AuditTab = 'changes' | 'emails'

export function AuditPage() {
  const s = useStyles()
  const { data: me } = useMe()
  const [tab, setTab] = useState<AuditTab>('changes')
  const emailEnabled = me?.emailEnabled === true

  return (
    <div className={s.root}>
      <Title2>Audit</Title2>
      <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as AuditTab)}>
        <Tab value="changes">Changes</Tab>
        {emailEnabled && <Tab value="emails">Sent emails</Tab>}
      </TabList>

      {tab === 'changes' && <ChangesTab />}
      {tab === 'emails' && emailEnabled && <SentEmailsTab />}
    </div>
  )
}

function ChangesTab() {
  const s = useStyles()

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [change, setChange] = useState<AuditChangeType | undefined>(undefined)
  const [targetType, setTargetType] = useState<AuditTargetType | undefined>(undefined)

  const { data, isLoading, error } = useAuditLog({ page, pageSize, change, targetType })

  const [pageError, setPageError] = useState<string | null>(null)
  const [exporting, setExporting] = useState(false)

  const items = data?.items ?? []

  async function onExport() {
    setPageError(null)
    setExporting(true)
    try {
      await downloadFromUrl(auditExportUrl({ change, targetType }), 'audit-log.csv')
    } catch (e) {
      setPageError(formatApiError(e))
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <div className={s.toolbar}>
        <Toolbar>
          <Button appearance="primary" icon={<ArrowDownload20Regular />} disabled={exporting} onClick={onExport}>
            {exporting ? 'Exporting…' : 'Export CSV'}
          </Button>
        </Toolbar>
      </div>

      <div className={s.filters}>
        <div className={s.field}>
          <span className={s.fieldLabel}>Change type</span>
          <Dropdown
            className={s.filterDropdown}
            size="small"
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
            size="small"
            selectedOptions={[targetType ?? ALL]}
            value={targetType ?? 'All'}
            onOptionSelect={(_, d) => {
              setTargetType(d.optionValue === ALL ? undefined : (d.optionValue as AuditTargetType))
              setPage(1)
            }}
          >
            <Option value={ALL}>All</Option>
            {TARGET_TYPES.map(t => <Option key={t} value={t}>{t}</Option>)}
          </Dropdown>
        </div>
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {pageError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not complete the action</MessageBarTitle>
            {pageError}
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
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={5}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={5}>No changes recorded.</GridMessageRow>
          )}
          {items.map(entry => (
            <TableRow key={entry.id} className={s.row}>
              <TableCell className={s.colTime}>
                <span className={s.truncate}>{formatDateTime(entry.timestamp)}</span>
              </TableCell>
              <TableCell className={s.colChange}>
                <ChangeBadge change={entry.change} />
              </TableCell>
              <TableCell className={s.colTarget}>{entry.targetType}</TableCell>
              <TableCell className={s.cellId}><IdentityCell name={entry.targetName} id={entry.targetId} /></TableCell>
              <TableCell className={s.cellId}><IdentityCell name={entry.actorName} id={entry.actorId} /></TableCell>
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

function SentEmailsTab() {
  const s = useStyles()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [status, setStatus] = useState<EmailStatus | undefined>(undefined)

  const { data, isLoading, error } = useEmailOutbox({ page, pageSize, status })
  const drain = useDrainEmail()
  const [pageError, setPageError] = useState<string | null>(null)

  const items = data?.items ?? []

  async function onDrain() {
    setPageError(null)
    try {
      await drain.mutateAsync()
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  return (
    <>
      <div className={s.toolbar}>
        <Toolbar>
          <Button icon={<Send20Regular />} disabled={drain.isPending} onClick={onDrain}>
            {drain.isPending ? 'Sending…' : 'Send pending now'}
          </Button>
        </Toolbar>
      </div>

      <div className={s.filters}>
        <div className={s.field}>
          <span className={s.fieldLabel}>Status</span>
          <Dropdown
            className={s.filterDropdown}
            size="small"
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
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}
      {pageError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{pageError}</MessageBarBody>
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
                <span className={s.truncate}>{formatDateTime(m.createdAt)}</span>
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
  const color = change === 'Create' ? 'success' : change === 'Delete' ? 'danger' : 'brand'
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
