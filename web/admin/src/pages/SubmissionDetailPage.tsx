import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  Badge, Button, Card, Tab, TabList,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Title2, Tooltip, MessageBarBody, makeStyles, tokens,
} from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { LocalizedTime } from '../components/LocalizedTime'
import { formatApiError, localizeDiagnostics } from '../api/client'
import { ArrowLeft20Regular, Edit20Regular } from '@fluentui/react-icons'
import { useCapabilities, useMySubmission, useSubmission, useSubmissionHistory } from '../api/hooks'
import type { AuditLog } from '../api/types'
import { useTranslation } from 'react-i18next'

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
  const { t } = useTranslation()
  const nav = useNavigate()
  const { id } = useParams<{ id: string }>()
  const { has } = useCapabilities()
  // Without cross-service read we only ever touch the caller's own submissions.
  const isService = !has('submissions:read')

  // Mutually-exclusive queries so we only ever talk to the endpoint our role can use.
  const adminQuery = useSubmission(id, !isService)
  const myQuery = useMySubmission(id, isService)
  const { data, isLoading, error } = isService ? myQuery : adminQuery

  // The history endpoint is operator/admin-only; service callers never see the tab.
  const showHistory = !isService
  const [tab, setTab] = useState<'details' | 'history'>('details')
  const localizedWarnings = data ? localizeDiagnostics(data.warningDetails, data.warnings) : []

  return (
    <div className={s.root}>
      <div className={s.header}>
        <div className={s.headerLeft}>
          <Button as="a" appearance="subtle" icon={<ArrowLeft20Regular />}>
            <Link to="/submissions">{t('schemasSubmissions.common.back')}</Link>
          </Button>
          <Title2>{t('schemasSubmissions.common.submission')}</Title2>
        </div>
        {data && (
          <Button appearance="primary" icon={<Edit20Regular />} onClick={() => nav(`/submissions/${data.id}/edit`)}>
            {t('schemasSubmissions.common.edit')}
          </Button>
        )}
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{formatApiError(error)}</MessageBarBody></AutoScrollMessageBar>}

      {isLoading && <div>{t('schemasSubmissions.common.loading')}</div>}

      {data && (
        <>
          <TabList selectedValue={tab} onTabSelect={(_, d) => setTab(d.value as 'details' | 'history')}>
            <Tab value="details">{t('schemasSubmissions.submissionDetail.details')}</Tab>
            {showHistory && <Tab value="history">{t('schemasSubmissions.common.history')}</Tab>}
          </TabList>

          {tab === 'details' && (
            <div className={s.tabBody}>
              <Card>
                <div className={s.meta}>
                  <div><strong>{t('schemasSubmissions.common.id')}</strong><br /><code>{data.id}</code></div>
                  <div><strong>{t('schemasSubmissions.common.service')}</strong><br />{data.serviceName}</div>
                  <div><strong>{t('schemasSubmissions.common.submittedAt')}</strong><br /><LocalizedTime value={data.submittedAt} /></div>
                  <div><strong>{t('schemasSubmissions.common.replacedAt')}</strong><br /><LocalizedTime value={data.replacedAt} /></div>
                  <div><strong>{t('schemasSubmissions.common.samples')}</strong><br />{data.samples.length}</div>
                  <div><strong>{t('schemasSubmissions.common.warnings')}</strong><br />{localizedWarnings.length}</div>
                </div>
              </Card>

              {localizedWarnings.length > 0 && (
                <AutoScrollMessageBar intent="warning">
                  <MessageBarBody>
                    <ul className={s.warningsList}>
                      {localizedWarnings.map((w, i) => <li key={i}>{w}</li>)}
                    </ul>
                  </MessageBarBody>
                </AutoScrollMessageBar>
              )}

              <Table size="small">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>{t('schemasSubmissions.common.schema')}</TableHeaderCell>
                    <TableHeaderCell>{t('schemasSubmissions.common.value')}</TableHeaderCell>
                    <TableHeaderCell>{t('schemasSubmissions.common.sample')}</TableHeaderCell>
                    <TableHeaderCell>{t('schemasSubmissions.common.timestamp')}</TableHeaderCell>
                    <TableHeaderCell>{t('schemasSubmissions.common.note')}</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.samples.map((sample, i) => (
                    <TableRow key={i}>
                      <TableCell>{sample.schemaName}</TableCell>
                      <TableCell>{sample.valueName}</TableCell>
                      <TableCell><code>{formatValue(sample.value)}</code></TableCell>
                      <TableCell><LocalizedTime value={sample.timestamp} /></TableCell>
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
  const { t } = useTranslation()
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
            <TableHeaderCell>{t('schemasSubmissions.common.timestamp')}</TableHeaderCell>
            <TableHeaderCell>{t('schemasSubmissions.submissionDetail.change')}</TableHeaderCell>
            <TableHeaderCell>{t('schemasSubmissions.submissionDetail.changedBy')}</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={3}>{t('schemasSubmissions.common.loading')}</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={3}>{t('schemasSubmissions.submissionDetail.noChanges')}</GridMessageRow>
          )}
          {items.map(entry => (
            <TableRow key={entry.id}>
              <TableCell><LocalizedTime value={entry.timestamp} /></TableCell>
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
  const { t } = useTranslation()
  const color = change === 'Create' ? 'success' : change === 'Delete' ? 'danger' : 'brand'
  return <Badge appearance="outline" color={color}>{t(`schemasSubmissions.submissionDetail.changeType.${change}`)}</Badge>
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return '—'
  if (typeof v === 'string') return v
  return JSON.stringify(v)
}
