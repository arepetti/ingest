import { Link, useNavigate, useParams } from 'react-router-dom'
import { Button, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Title2, makeStyles, MessageBarBody, Card } from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError } from '../api/client'
import { ArrowLeft20Regular, Edit20Regular } from '@fluentui/react-icons'
import { useMe, useMySubmission, useSubmission } from '../api/hooks'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', gap: '12px', justifyContent: 'space-between' },
  headerLeft: { display: 'flex', alignItems: 'center', gap: '12px' },
  meta: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '12px', padding: '16px' },
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
          <Card>
            <div className={s.meta}>
              <div><strong>ID</strong><br /><code>{data.id}</code></div>
              <div><strong>Service</strong><br />{data.serviceName}</div>
              <div><strong>Submitted at</strong><br />{new Date(data.submittedAt).toLocaleString()}</div>
              <div><strong>Replaced at</strong><br />{data.replacedAt ? new Date(data.replacedAt).toLocaleString() : '—'}</div>
              <div><strong>Samples</strong><br />{data.samples.length}</div>
            </div>
          </Card>

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
        </>
      )}
    </div>
  )
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return '—'
  if (typeof v === 'string') return v
  return JSON.stringify(v)
}
