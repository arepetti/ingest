import { Badge, Card, CardHeader, makeStyles, Subtitle2, Text, Title2, Tooltip, tokens } from '@fluentui/react-components'
import {
  useAccounts, useMe, useMissingSubmissions, useMySubmissions, useSchemas, useSubmissions,
} from '../api/hooks'
import type { MissingByCadence, MissingSubmissionEntry } from '../api/types'
import { cadenceLabel } from '../utils/cadence'
import { Link } from 'react-router-dom'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '24px' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: '16px' },
  // Missing-submission cards need more room for the inline list, so they live in a wider grid.
  missingGrid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: '16px' },
  card: { padding: '20px' },
  big: { fontSize: '32px', fontWeight: 700, color: tokens.colorBrandForeground1 },
  sub: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  missingHeaderRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' },
  missingCount: { fontSize: '28px', fontWeight: 700, color: tokens.colorPaletteRedForeground1 },
  missingList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    marginTop: '8px',
    // Cap the inline list so a registry with dozens of missing entries doesn't blow up the card.
    maxHeight: '220px',
    overflowY: 'auto',
    paddingRight: '4px',
  },
  missingEntry: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    padding: '4px 0',
    borderBottom: `1px solid ${tokens.colorNeutralStroke3}`,
    fontSize: '13px',
  },
  missingEntryName: {
    flex: '1 1 auto',
    minWidth: 0,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
})

export function Dashboard() {
  const s = useStyles()
  const { data: me } = useMe()
  const isService = me?.role === 'Service'

  // Service callers can't hit admin listings — gate the queries by role to avoid pointless 403s in the UI.
  // The dashboard role cards count by `AccountRole` and ignore `AccountKind` (so a Service-role
  // can be either an automated Application or an interactive User and still be tallied here).
  const services = useAccounts({ role: 'Service' }, !isService)
  const operators = useAccounts({ role: 'Operator' }, !isService)
  const admins = useAccounts({ role: 'Admin' }, !isService)
  const schemas = useSchemas(undefined, !isService)
  const adminSubs = useSubmissions({ page: 1, pageSize: 1 }, !isService)
  const mySubs = useMySubmissions({ page: 1, pageSize: 1 }, !!isService)
  const missing = useMissingSubmissions(!isService)

  return (
    <div className={s.root}>
      <Title2>{isService ? `Welcome, ${me?.label || me?.name || ''}` : 'Overview'}</Title2>
      <div className={s.grid}>
        {!isService && (
          <>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">Services</Text>} />
              <div className={s.big}>{services.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/services">Manage</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">Operators</Text>} />
              <div className={s.big}>{operators.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/services">Manage</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">Admins</Text>} />
              <div className={s.big}>{admins.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/services">Manage</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">Schemas</Text>} />
              <div className={s.big}>{schemas.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/schemas">Manage</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">Submissions</Text>} />
              <div className={s.big}>{adminSubs.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/submissions">Browse</Link>
              </div>
            </Card>
          </>
        )}

        {isService && (
          <>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">My submissions</Text>} />
              <div className={s.big}>{mySubs.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/submissions">Browse</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">Submit data</Text>} />
              <div className={s.sub} style={{ marginTop: 8 }}>
                <Link to="/submissions/new">New submission →</Link>
              </div>
            </Card>
          </>
        )}
      </div>

      {!isService && <MissingSubmissionsSection
        loading={missing.isLoading}
        buckets={missing.data}
        styles={s}
      />}
    </div>
  )
}

/**
 * Operator-only cluster of cards summarising required submissions that aren't in yet, one card
 * per cadence the server reports has unsatisfied entries. Renders nothing when the report is
 * still loading and nothing when everything is up to date — both signals are useful but neither
 * warrants chrome on a clean dashboard.
 */
function MissingSubmissionsSection({
  loading,
  buckets,
  styles,
}: {
  loading: boolean
  buckets?: MissingByCadence[]
  styles: ReturnType<typeof useStyles>
}) {
  if (loading) return null
  if (!buckets || buckets.length === 0) return null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <Subtitle2>Missing submissions</Subtitle2>
      <div className={styles.missingGrid}>
        {buckets.map((b) => (
          <MissingCadenceCard key={b.cadence} bucket={b} styles={styles} />
        ))}
      </div>
    </div>
  )
}

function MissingCadenceCard({
  bucket,
  styles,
}: {
  bucket: MissingByCadence
  styles: ReturnType<typeof useStyles>
}) {
  return (
    <Card className={styles.card}>
      <CardHeader
        header={
          <div className={styles.missingHeaderRow}>
            <Text weight="semibold">{cadenceLabel(bucket.cadence)}</Text>
            <Badge appearance="outline" color="informative">
              {windowLabel(bucket.periodStart, bucket.periodEnd)}
            </Badge>
          </div>
        }
      />
      <div className={styles.missingCount}>{bucket.entries.length}</div>
      <div className={styles.sub}>
        {bucket.entries.length === 1 ? 'service short on submissions' : 'services short on submissions'}
      </div>
      <div className={styles.missingList}>
        {bucket.entries.map((e) => (
          <MissingEntryRow key={`${e.serviceId}-${e.schemaName}`} entry={e} styles={styles} />
        ))}
      </div>
    </Card>
  )
}

function MissingEntryRow({
  entry,
  styles,
}: {
  entry: MissingSubmissionEntry
  styles: ReturnType<typeof useStyles>
}) {
  const serviceLabel = entry.serviceLabel || entry.serviceName
  const schemaLabel = entry.schemaLabel || entry.schemaName
  const label = `${serviceLabel} • ${schemaLabel}`
  return (
    <div className={styles.missingEntry}>
      <Tooltip content={label} relationship="label">
        <Link
          to={`/services/${encodeURIComponent(entry.serviceName)}/status`}
          className={styles.missingEntryName}
          // Inline-styling the link colour keeps the dashboard CSS scoped and avoids leaking a
          // theme override into every Link in the app.
          style={{ color: tokens.colorBrandForeground1, textDecoration: 'none' }}
        >
          {label}
        </Link>
      </Tooltip>
      <Badge appearance="outline" color="severe">
        {entry.missingRequiredCount}/{entry.totalRequiredCount}
      </Badge>
    </div>
  )
}

/**
 * Short human-readable label for the current cadence window. Compact on purpose so it fits
 * inside the card header next to the cadence name without wrapping.
 */
function windowLabel(start: string, end: string): string {
  try {
    const s = new Date(start)
    const e = new Date(end)
    // Weekly/monthly windows look best as "May 25 – Jun 1"; for single-day buckets collapse to "May 28".
    const sameMonth = s.getUTCMonth() === e.getUTCMonth() && s.getUTCFullYear() === e.getUTCFullYear()
    const endInclusive = new Date(e.getTime() - 1)
    const fmt = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' })
    if (sameMonth && s.getUTCDate() === endInclusive.getUTCDate()) return fmt.format(s)
    return `${fmt.format(s)} – ${fmt.format(endInclusive)}`
  } catch {
    return ''
  }
}
