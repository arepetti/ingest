import {
  Badge, Card, CardHeader, Dropdown, Field, makeStyles, Option, Spinner, Subtitle2, Text, Title2, Tooltip, tokens,
} from '@fluentui/react-components'
import {
  CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip as RechartsTooltip, XAxis, YAxis,
} from 'recharts'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  useAccounts, useCapabilities, useMissingHistory, useMissingSubmissions, useMySubmissions, usePendingApprovalCount, useSchemas, useSubmissions,
} from '../api/hooks'
import type { Cadence, MissingByCadence, MissingSubmissionEntry } from '../api/types'
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
  chartCard: { padding: '20px', display: 'flex', flexDirection: 'column', gap: '16px' },
  chartToolbar: { display: 'flex', alignItems: 'flex-end', gap: '16px', flexWrap: 'wrap' },
  chartFilter: { minWidth: '180px' },
  chartBody: { width: '100%', height: '320px' },
  chartEmpty: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '320px',
    color: tokens.colorNeutralForeground3,
    fontSize: '13px',
  },
})

export function Dashboard() {
  const s = useStyles()
  const { t } = useTranslation()
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
      <Title2>{selfService
        ? t('analytics.dashboard.welcome', { name: me?.label || me?.name || '' })
        : t('analytics.dashboard.title')}</Title2>
      <div className={s.grid}>
        {canApprove && (
          <Card className={pendingCount > 0 ? `${s.pendingCard} ${s.pendingCardWarning}` : s.pendingCard}>
            <CardHeader header={<Text weight="semibold">{t('analytics.dashboard.cards.pendingApprovals')}</Text>} />
            <div className={pendingCount > 0 ? s.bigWarning : s.big}>{pending.isLoading ? '—' : pendingCount}</div>
            <div className={s.sub}>
              <Link to="/submissions?approvalStatus=Pending">{t('analytics.dashboard.actions.review')}</Link>
            </div>
          </Card>
        )}
        {canReadAccounts && (
          <>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">{t('analytics.common.services')}</Text>} />
              <div className={s.big}>{services.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/services">{t('analytics.dashboard.actions.manage')}</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">{t('analytics.dashboard.cards.operators')}</Text>} />
              <div className={s.big}>{operators.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/services">{t('analytics.dashboard.actions.manage')}</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">{t('analytics.dashboard.cards.admins')}</Text>} />
              <div className={s.big}>{admins.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/services">{t('analytics.dashboard.actions.manage')}</Link>
              </div>
            </Card>
          </>
        )}
        {canReadSchemas && (
          <Card className={s.card}>
            <CardHeader header={<Text weight="semibold">{t('analytics.explore.filters.schemas')}</Text>} />
            <div className={s.big}>{schemas.data?.total ?? '—'}</div>
            <div className={s.sub}>
              <Link to="/schemas">{t('analytics.dashboard.actions.manage')}</Link>
            </div>
          </Card>
        )}
        {canReadSubmissions && (
          <Card className={s.card}>
            <CardHeader header={<Text weight="semibold">{t('analytics.dashboard.cards.submissions')}</Text>} />
            <div className={s.big}>{adminSubs.data?.total ?? '—'}</div>
            <div className={s.sub}>
              <Link to="/submissions">{t('analytics.dashboard.actions.browse')}</Link>
            </div>
          </Card>
        )}

        {selfService && (
          <>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">{t('analytics.dashboard.cards.mySubmissions')}</Text>} />
              <div className={s.big}>{mySubs.data?.total ?? '—'}</div>
              <div className={s.sub}>
                <Link to="/submissions">{t('analytics.dashboard.actions.browse')}</Link>
              </div>
            </Card>
            <Card className={s.card}>
              <CardHeader header={<Text weight="semibold">{t('analytics.dashboard.cards.submitData')}</Text>} />
              <div className={s.sub} style={{ marginTop: 8 }}>
                <Link to="/submissions/new">{t('analytics.dashboard.actions.newSubmission')}</Link>
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

      {canReadStatus && <MissingTrendsChart styles={s} canReadAccounts={canReadAccounts} />}

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
  const { t } = useTranslation()
  if (loading) return null
  if (!buckets || buckets.length === 0) return null

  const current = buckets.filter((b) => b.period === 'Current')
  const previous = buckets.filter((b) => b.period === 'Previous')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      <div className={styles.sectionHeaderRow}>
        <Subtitle2>{t('analytics.missing.titleShort')}</Subtitle2>
        <Link to="/missing" style={{ color: tokens.colorBrandForeground1, fontSize: 13 }}>
          {t('analytics.dashboard.actions.viewByPeriod')}
        </Link>
      </div>

      {previous.length > 0 && (
        <MissingPeriodGroup
          title={t('analytics.dashboard.missing.overdueTitle')}
          subtitle={t('analytics.dashboard.missing.overdueDescription')}
          buckets={previous}
          tone="previous"
          styles={styles}
        />
      )}

      {current.length > 0 && (
        <MissingPeriodGroup
          title={t('analytics.dashboard.missing.currentTitle')}
          subtitle={t('analytics.dashboard.missing.currentDescription')}
          buckets={current}
          tone="current"
          styles={styles}
        />
      )}
    </div>
  )
}

// Cadences offered in the trend's cadence picker, in the same order as the server enum. Monthly
// is the default because it's the most common reporting rhythm for operational KPIs.
const TREND_CADENCES: Cadence[] = ['Daily', 'Weekly', 'Fortnightly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

// Sentinel option value for the "all services" (global) view in the service picker.
const ALL_SERVICES = '__all__'

/**
 * "Missing submissions over time" trend. Plots the total count of missing required values per
 * cadence window (oldest → current) as a line chart, either across every service (the global
 * view) or scoped to a single service. Both filters drive the same `/missing/history` endpoint.
 */
function MissingTrendsChart({
  styles,
  canReadAccounts,
}: {
  styles: ReturnType<typeof useStyles>
  canReadAccounts: boolean
}) {
  const { t, i18n } = useTranslation()
  const [cadence, setCadence] = useState<Cadence>('Monthly')
  const [serviceId, setServiceId] = useState<string>(ALL_SERVICES)

  // Only operators with accounts:read can resolve the service list; everyone else keeps the
  // global view (the chart still works, it just can't offer the per-service breakdown).
  const services = useAccounts({ role: 'Service' }, canReadAccounts)
  const scoped = serviceId !== ALL_SERVICES ? serviceId : undefined
  const history = useMissingHistory(cadence, 12, scoped)

  const rows = useMemo(
    () => (history.data?.points ?? []).map(p => ({
      label: trendPointLabel(p.periodStart, cadence, i18n.language),
      missing: p.totalMissing,
    })),
    [history.data, cadence, i18n.language],
  )

  const hasData = rows.length > 0
  const serviceName = serviceId === ALL_SERVICES
    ? t('analytics.common.allServices')
    : services.data?.items.find(a => a.id === serviceId)?.label
      || services.data?.items.find(a => a.id === serviceId)?.name
      || t('analytics.common.service')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
      <Subtitle2>{t('analytics.dashboard.trend.title')}</Subtitle2>
      <Card className={styles.chartCard}>
        <div className={styles.chartToolbar}>
          <Field label={t('analytics.common.cadence')} className={styles.chartFilter}>
            <Dropdown
              selectedOptions={[cadence]}
              value={t(`analytics.cadence.${cadence.toLowerCase()}`)}
              onOptionSelect={(_, d) => d.optionValue && setCadence(d.optionValue as Cadence)}
            >
              {TREND_CADENCES.map(c => <Option key={c} value={c}>{t(`analytics.cadence.${c.toLowerCase()}`)}</Option>)}
            </Dropdown>
          </Field>
          {canReadAccounts && (
            <Field label={t('analytics.common.service')} className={styles.chartFilter}>
              <Dropdown
                selectedOptions={[serviceId]}
                value={serviceName}
                onOptionSelect={(_, d) => d.optionValue && setServiceId(d.optionValue)}
              >
                <Option value={ALL_SERVICES}>{t('analytics.common.allServices')}</Option>
                {(services.data?.items ?? []).map(a => (
                  <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
                ))}
              </Dropdown>
            </Field>
          )}
        </div>

        {history.isLoading ? (
          <div className={styles.chartEmpty}><Spinner size="tiny" label={t('analytics.dashboard.trend.loading')} /></div>
        ) : !hasData ? (
          <div className={styles.chartEmpty}>{t('analytics.dashboard.trend.empty')}</div>
        ) : (
          <div className={styles.chartBody}>
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={rows} margin={{ top: 8, right: 24, bottom: 8, left: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} />
                <XAxis dataKey="label" tick={{ fontSize: 11 }} interval="preserveStartEnd" angle={-30} textAnchor="end" height={56} />
                <YAxis tick={{ fontSize: 11 }} allowDecimals={false} />
                <RechartsTooltip />
                <Line
                  type="monotone"
                  dataKey="missing"
                  name={t('analytics.dashboard.trend.seriesName', { service: serviceName })}
                  stroke={tokens.colorBrandForeground1}
                  strokeWidth={2}
                  dot={{ r: 2 }}
                  connectNulls
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </Card>
    </div>
  )
}

/**
 * Compact axis label for one trend point. Sub-monthly cadences read best as "MMM d" (the window
 * changes within a month); monthly-and-longer windows collapse to "MMM yyyy" so a 12-point yearly
 * trend doesn't repeat the same month label.
 */
function trendPointLabel(periodStart: string, cadence: Cadence, locale: string): string {
  try {
    const d = new Date(periodStart)
    const subMonthly = cadence === 'Daily' || cadence === 'Weekly' || cadence === 'Fortnightly'
    const fmt = subMonthly
      ? new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric' })
      : new Intl.DateTimeFormat(locale, { month: 'short', year: 'numeric' })
    return fmt.format(d)
  } catch {
    return ''
  }
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
  const { t, i18n } = useTranslation()
  const countClass = tone === 'previous'
    ? `${styles.missingCount} ${styles.missingCountPrevious}`
    : `${styles.missingCount} ${styles.missingCountCurrent}`
  return (
    <Card className={styles.card}>
      <CardHeader
        header={
          <div className={styles.missingHeaderRow}>
            <Text weight="semibold">{t(`analytics.cadence.${bucket.cadence.toLowerCase()}`)}</Text>
            <Badge appearance="outline" color={tone === 'previous' ? 'danger' : 'warning'}>
              {windowLabel(bucket.periodStart, bucket.periodEnd, i18n.language)}
            </Badge>
          </div>
        }
      />
      <div className={countClass}>{bucket.entries.length}</div>
      <div className={styles.sub}>
        {t('analytics.dashboard.missing.servicesShort', { count: bucket.entries.length })}
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
function windowLabel(start: string, end: string, locale: string): string {
  try {
    const s = new Date(start)
    const e = new Date(end)
    // Weekly/monthly windows look best as "May 25 – Jun 1"; for single-day buckets collapse to "May 28".
    const sameMonth = s.getUTCMonth() === e.getUTCMonth() && s.getUTCFullYear() === e.getUTCFullYear()
    const endInclusive = new Date(e.getTime() - 1)
    const fmt = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric' })
    if (sameMonth && s.getUTCDate() === endInclusive.getUTCDate()) return fmt.format(s)
    return `${fmt.format(s)} – ${fmt.format(endInclusive)}`
  } catch {
    return ''
  }
}
