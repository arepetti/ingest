import { Badge, Card, CardHeader, makeStyles, Subtitle2, Text, Title2, Tooltip, tokens } from '@fluentui/react-components'
import {
  useAccounts, useCapabilities, useMissingSubmissions, useMySubmissions, usePendingApprovalCount, useSchemas, useSubmissions,
} from '../api/hooks'
import type { MissingByCadence, MissingSubmissionEntry } from '../api/types'
import { cadenceLabel } from '../utils/cadence'
import { Link } from 'react-router-dom'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '24px', minHeight: '100%' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: '16px' },
  // Missing-submission cards need more room for the inline list, so they live in a wider grid.
  missingGrid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: '16px' },
  card: { padding: '20px' },
  // Pending-approvals card. The amber accent only shows when there's actually a backlog, so an
  // empty queue reads as calm/neutral like the other count cards.
  pendingCard: {
    padding: '20px',
  },
  pendingCardWarning: {
    borderTop: `3px solid ${tokens.colorStatusWarningBorder1}`,
  },
  big: { fontSize: '32px', fontWeight: 700, color: tokens.colorBrandForeground1 },
  bigWarning: { fontSize: '32px', fontWeight: 700, color: tokens.colorStatusWarningForeground1 },
  sub: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  missingHeaderRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' },
  missingCount: { fontSize: '28px', fontWeight: 700 },
  // Current period is still open → amber/warning; the previous (closed) period is overdue → red.
  missingCountCurrent: { color: tokens.colorStatusWarningForeground1 },
  missingCountPrevious: { color: tokens.colorStatusDangerForeground1 },
  sectionHeaderRow: { display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
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
  footer: {
    marginTop: 'auto',
    paddingTop: '16px',
    textAlign: 'center',
    color: tokens.colorNeutralForeground4,
    fontSize: '11px',
  },
})

export function Dashboard() {
  const s = useStyles()
  const { me, has } = useCapabilities()
  // Only callers with the approve capability can act on the queue, and only when the workflow is on.
  const canApprove = !!me?.approvalEnabled && has('submissions:approve')
  const pending = usePendingApprovalCount(canApprove)
  const pendingCount = pending.data?.count ?? 0

  // Each summary card is gated by the capability backing its page; a self-service submitter (no
  // cross-service read) instead sees the "my submissions" cards. Gate the queries the same way to
  // avoid pointless 403s in the UI.
  const canReadAccounts = has('accounts:read')
  const canReadSchemas = has('schemas:read')
  const canReadSubmissions = has('submissions:read')
  const canReadStatus = has('status:read')
  const selfService = !canReadSubmissions

  // The dashboard role cards count by `AccountRole` and ignore `AccountKind`.
  const services = useAccounts({ role: 'Service' }, canReadAccounts)
  const operators = useAccounts({ role: 'Operator' }, canReadAccounts)
  const admins = useAccounts({ role: 'Admin' }, canReadAccounts)
  const schemas = useSchemas(undefined, canReadSchemas)
  const adminSubs = useSubmissions({ page: 1, pageSize: 1 }, canReadSubmissions)
  const mySubs = useMySubmissions({ page: 1, pageSize: 1 }, selfService)
  const missing = useMissingSubmissions(canReadStatus)

  return (
    <div className={s.root}>
      <Title2>{selfService ? `Welcome, ${me?.label || me?.name || ''}` : 'Overview'}</Title2>
      <div className={s.grid}>
        {canApprove && (
          <Card className={pendingCount > 0 ? `${s.pendingCard} ${s.pendingCardWarning}` : s.pendingCard}>
            <CardHeader header={<Text weight="semibold">Pending approvals</Text>} />
            <div className={pendingCount > 0 ? s.bigWarning : s.big}>{pending.isLoading ? '—' : pendingCount}</div>
            <div className={s.sub}>
              <Link to="/submissions?approvalStatus=Pending">Review</Link>
            </div>
          </Card>
        )}
        {canReadAccounts && (
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
          </>
        )}
        {canReadSchemas && (
          <Card className={s.card}>
            <CardHeader header={<Text weight="semibold">Schemas</Text>} />
            <div className={s.big}>{schemas.data?.total ?? '—'}</div>
            <div className={s.sub}>
              <Link to="/schemas">Manage</Link>
            </div>
          </Card>
        )}
        {canReadSubmissions && (
          <Card className={s.card}>
            <CardHeader header={<Text weight="semibold">Submissions</Text>} />
            <div className={s.big}>{adminSubs.data?.total ?? '—'}</div>
            <div className={s.sub}>
              <Link to="/submissions">Browse</Link>
            </div>
          </Card>
        )}

        {selfService && (
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

      {canReadStatus && <MissingSubmissionsSection
        loading={missing.isLoading}
        buckets={missing.data}
        styles={s}
      />}

      <div className={s.footer}>Ingest{me?.version ? ` v${me.version}` : ''}</div>
    </div>
  )
}

/**
 * Operator-only cluster of cards summarising required submissions that aren't in yet, split into
 * the current (still-open) window and the previous (closed, overdue) window. Renders nothing
 * while loading and nothing when everything is up to date — both signals are useful but neither
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

  const current = buckets.filter((b) => b.period === 'Current')
  const previous = buckets.filter((b) => b.period === 'Previous')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      <div className={styles.sectionHeaderRow}>
        <Subtitle2>Missing submissions</Subtitle2>
        <Link to="/missing" style={{ color: tokens.colorBrandForeground1, fontSize: 13 }}>
          View by period →
        </Link>
      </div>

      {previous.length > 0 && (
        <MissingPeriodGroup
          title="Overdue — previous period"
          subtitle="These submission windows have closed."
          buckets={previous}
          tone="previous"
          styles={styles}
        />
      )}

      {current.length > 0 && (
        <MissingPeriodGroup
          title="This period"
          subtitle="Still within the submission window."
          buckets={current}
          tone="current"
          styles={styles}
        />
      )}
    </div>
  )
}

type MissingTone = 'current' | 'previous'

function MissingPeriodGroup({
  title,
  subtitle,
  buckets,
  tone,
  styles,
}: {
  title: string
  subtitle: string
  buckets: MissingByCadence[]
  tone: MissingTone
  styles: ReturnType<typeof useStyles>
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div>
        <Text weight="semibold">{title}</Text>
        <div className={styles.sub}>{subtitle}</div>
      </div>
      <div className={styles.missingGrid}>
        {buckets.map((b) => (
          <MissingCadenceCard key={`${tone}-${b.cadence}`} bucket={b} tone={tone} styles={styles} />
        ))}
      </div>
    </div>
  )
}

function MissingCadenceCard({
  bucket,
  tone,
  styles,
}: {
  bucket: MissingByCadence
  tone: MissingTone
  styles: ReturnType<typeof useStyles>
}) {
  const countClass = tone === 'previous'
    ? `${styles.missingCount} ${styles.missingCountPrevious}`
    : `${styles.missingCount} ${styles.missingCountCurrent}`
  return (
    <Card className={styles.card}>
      <CardHeader
        header={
          <div className={styles.missingHeaderRow}>
            <Text weight="semibold">{cadenceLabel(bucket.cadence)}</Text>
            <Badge appearance="outline" color={tone === 'previous' ? 'danger' : 'warning'}>
              {windowLabel(bucket.periodStart, bucket.periodEnd)}
            </Badge>
          </div>
        }
      />
      <div className={countClass}>{bucket.entries.length}</div>
      <div className={styles.sub}>
        {bucket.entries.length === 1 ? 'service short on submissions' : 'services short on submissions'}
      </div>
      <div className={styles.missingList}>
        {bucket.entries.map((e) => (
          <MissingEntryRow key={`${e.serviceId}-${e.schemaName}`} entry={e} tone={tone} styles={styles} />
        ))}
      </div>
    </Card>
  )
}

function MissingEntryRow({
  entry,
  tone,
  styles,
}: {
  entry: MissingSubmissionEntry
  tone: MissingTone
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
      <Badge appearance="outline" color={tone === 'previous' ? 'danger' : 'warning'}>
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
