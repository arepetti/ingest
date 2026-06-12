import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  Badge, Button, Card, Tab, TabList,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Title2, Tooltip, MessageBarBody, makeStyles, tokens,
} from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { formatApiError } from '../api/client'
import { formatDateTime } from '../utils/format'
import { ArrowLeft20Regular, Edit20Regular } from '@fluentui/react-icons'
import { useMe, useMySubmission, useSubmission, useSubmissionHistory } from '../api/hooks'
import type { AuditLog } from '../api/types'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', gap: '12px', justifyContent: 'space-between' },
  headerLeft: { display: 'flex', alignItems: 'center', gap: '12px' },
  meta: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px', padding: '16px' },
  tabBody: { display: 'flex', flexDirection: 'column', gap: '16px' },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  warningsList: { margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '4px' },
})

export function SubmissionDetailPage() {
  const s = useStyles()
  const nav = useNavigate()
  const { id } = useParams<{ id: string }>()
  const { data: me } = useMe()
  const isService = me?.role === 'Service'

  // Mutually-exclusive queries so we only ever talk to the endpoint our role can use.
  const adminQuery = useSubmission(id, !isService)
  const myQuery = useMySubmission(id, isService)
  const { data, isLoading, error } = isService ? myQuery : adminQuery

  // The history endpoint is operator/admin-only; service callers never see the tab.
  const showHistory = !isService
  const [tab, setTab] = useState<'details' | 'history'>('details')

  return (
    <div className={s.root}>
      <div className={s.header}>
        <div className={s.headerLeft}>
          <Button as="a" appearance="subtle" icon={<ArrowLeft20Regular />}>
            <Link to="/submissions">Back</Link>
          </Button>
          <Title2>Submission</Title2>
        </div>
        {data && (
          <Button appearance="primary" icon={<Edit20Regular />} onClick={() => nav(`/submissions/${data.id}/edit`)}>
            Edit
          </Button>
        )}
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{formatApiError(error)}</MessageBarBody></AutoScrollMessageBar>}

      {isLoading && <div>Loading...</div>}

      {data && (
        <>
          <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as 'details' | 'history')}>
            <Tab value="details">Details</Tab>
            {showHistory && <Tab value="history">History</Tab>}
          </TabList>

          {tab === 'details' && (
            <div className={s.tabBody}>
              <Card>
                <div className={s.meta}>
                  <div><strong>ID</strong><br /><code>{data.id}</code></div>
                  <div><strong>Service</strong><br />{data.serviceName}</div>
                  <div><strong>Submitted at</strong><br />{new Date(data.submittedAt).toLocaleString()}</div>
                  <div><strong>Replaced at</strong><br />{data.replacedAt ? new Date(data.replacedAt).toLocaleString() : '—'}</div>
                  <div><strong>Samples</strong><br />{data.samples.length}</div>
                  <div><strong>Warnings</strong><br />{data.warnings?.length ?? 0}</div>
                </div>
              </Card>

              {(data.warnings?.length ?? 0) > 0 && (
                <AutoScrollMessageBar intent="warning">
                  <MessageBarBody>
                    <ul className={s.warningsList}>
                      {data.warnings.map((w, i) => <li key={i}>{w}</li>)}
                    </ul>
                  </MessageBarBody>
                </AutoScrollMessageBar>
              )}

              <Table size="small">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Schema</TableHeaderCell>
                    <TableHeaderCell>Value</TableHeaderCell>
                    <TableHeaderCell>Sample</TableHeaderCell>
                    <TableHeaderCell>Timestamp</TableHeaderCell>
                    <TableHeaderCell>Note</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.samples.map((sample, i) => (
                    <TableRow key={i}>
                      <TableCell>{sample.schemaName}</TableCell>
                      <TableCell>{sample.valueName}</TableCell>
                      <TableCell><code>{formatValue(sample.value)}</code></TableCell>
                      <TableCell>{new Date(sample.timestamp).toLocaleString()}</TableCell>
                      <TableCell>{sample.note ?? '—'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}

          {tab === 'history' && showHistory && id && <HistoryTab id={id} />}
        </>
      )}
    </div>
  )
}

function HistoryTab({ id }: { id: string }) {
  const s = useStyles()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error } = useSubmissionHistory(id, { page, pageSize })

  const items = data?.items ?? []

  return (
    <div className={s.tabBody}>
      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{formatApiError(error)}</MessageBarBody></AutoScrollMessageBar>}

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Timestamp</TableHeaderCell>
            <TableHeaderCell>Change</TableHeaderCell>
            <TableHeaderCell>Changed by</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={3}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={3}>No changes recorded.</GridMessageRow>
          )}
          {items.map(entry => (
            <TableRow key={entry.id}>
              <TableCell>{formatDateTime(entry.timestamp)}</TableCell>
              <TableCell><ChangeBadge change={entry.change} /></TableCell>
              <TableCell>
                {entry.actorName ?? (entry.actorId
                  ? <Tooltip content={entry.actorId} relationship="label"><span className={s.mono}>{entry.actorId}</span></Tooltip>
                  : '—')}
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
    </div>
  )
}

function ChangeBadge({ change }: { change: AuditLog['change'] }) {
  const color = change === 'Create' ? 'success' : change === 'Delete' ? 'danger' : 'brand'
  return <Badge appearance="outline" color={color}>{change}</Badge>
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return '—'
  if (typeof v === 'string') return v
  return JSON.stringify(v)
}
