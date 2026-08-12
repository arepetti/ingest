import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { Link } from 'react-router-dom'
import {
  Badge, Card, CardHeader, Dropdown, Field, MessageBar, MessageBarBody, Option,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Text, Title2, makeStyles, tokens,
} from '@fluentui/react-components'
import { ArrowClockwise20Regular, ArrowDownload20Regular, MoreHorizontal20Regular } from '@fluentui/react-icons'
import {
  Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError } from '../api/client'
import { useMissingHistory, useMissingPeriod } from '../api/hooks'
import type { Cadence, MissingHistoryPoint, MissingSubmissionEntry } from '../api/types'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'

// All cadences in their natural (enum) order so the picker reads daily → yearly.
const CADENCES: Cadence[] = ['Daily', 'Weekly', 'Fortnightly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

// How many windows the trend chart looks back over. Matches the server-side default.
const PERIODS = 12

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '20px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  filters: { display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))', gap: '16px' },
  card: { padding: '16px' },
  cardHeader: { marginBottom: '8px' },
  cardSub: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  empty: { color: tokens.colorNeutralForeground3, padding: '24px 0', textAlign: 'center' },
  totals: { display: 'flex', gap: '24px', alignItems: 'baseline', flexWrap: 'wrap' },
  bigNumber: { fontSize: '32px', fontWeight: 700, color: tokens.colorStatusDangerForeground1 },
  link: { color: tokens.colorBrandForeground1, textDecoration: 'none' },
})

export function MissingSubmissionsPage() {
  const s = useStyles()
  const { t } = useTranslation()
  const [cadence, setCadence] = useState<Cadence>('Monthly')
  // Default to the previous (closed, overdue) window — that's the actionable one.
  const [offset, setOffset] = useState(-1)

  const history = useMissingHistory(cadence, PERIODS)
  const period = useMissingPeriod(cadence, offset)

  const trend = useMemo(
    () => (history.data?.points ?? []).map((p) => ({
      label: periodLabel(p.periodStart, cadence),
      offset: p.offset,
      missing: p.totalMissing,
    })),
    [history.data, cadence],
  )

  // Roll the per-(service, schema) entries up to a per-service total for the selected period.
  const byService = useMemo(() => {
    const map = new Map<string, { name: string; missing: number }>()
    for (const e of period.data?.entries ?? []) {
      const cur = map.get(e.serviceId) ?? { name: e.serviceLabel || e.serviceName, missing: 0 }
      cur.missing += e.missingRequiredCount
      map.set(e.serviceId, cur)
    }
    return [...map.values()].sort((a, b) => b.missing - a.missing)
  }, [period.data])

  const periodOptions = history.data?.points ?? []
  const selectedPoint = periodOptions.find((p) => p.offset === offset)
  const periodText = selectedPoint ? periodOptionLabel(selectedPoint, cadence, t) : ''
  const totalMissing = period.data?.entries.reduce((acc, e) => acc + e.missingRequiredCount, 0) ?? 0

  const [exportError, setExportError] = useState<string | null>(null)
  const entries = period.data?.entries ?? []
  const exportColumns = useMemo<ExportColumn<MissingSubmissionEntry>[]>(() => [
    { header: t('analytics.common.service'), value: e => e.serviceLabel || e.serviceName },
    { header: t('analytics.common.schema'), value: e => e.schemaLabel || e.schemaName },
    { header: t('analytics.missing.columns.missing'), value: e => e.missingRequiredCount },
    { header: t('analytics.missing.columns.totalRequired'), value: e => e.totalRequiredCount },
  ], [t])
  const missingExport = useCsvExport({
    filename: `missing-submissions-${cadence}.csv`,
    columns: exportColumns,
    fetchAll: () => Promise.resolve(entries),
    onError: setExportError,
  })

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Title2>{t('analytics.missing.title')}</Title2>
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label={t('analytics.common.moreActions')} />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => { history.refetch(); period.refetch() }}>{t('analytics.common.refresh')}</MenuItem>
              <MenuDivider />
              <MenuItem
                icon={<ArrowDownload20Regular />}
                disabled={missingExport.exporting || entries.length === 0}
                onClick={missingExport.exportList}
              >
                {missingExport.exporting ? t('analytics.common.exporting') : t('analytics.common.exportView')}
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>

      {(history.error || period.error) && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{formatApiError(history.error || period.error)}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {exportError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{exportError}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <div className={s.filters}>
        <Field label={t('analytics.common.cadence')}>
          <Dropdown
            selectedOptions={[cadence]}
            value={t(`analytics.cadence.${cadence.toLowerCase()}`)}
            onOptionSelect={(_, d) => setCadence((d.optionValue as Cadence) ?? 'Monthly')}
          >
            {CADENCES.map((c) => (
              <Option key={c} value={c}>{t(`analytics.cadence.${c.toLowerCase()}`)}</Option>
            ))}
          </Dropdown>
        </Field>
        <Field label={t('analytics.common.period')}>
          <Dropdown
            selectedOptions={[String(offset)]}
            value={periodText}
            onOptionSelect={(_, d) => setOffset(Number(d.optionValue ?? -1))}
          >
            {/* Newest first so "current" / "previous" sit at the top of the list. */}
            {[...periodOptions].reverse().map((p) => (
              <Option key={p.offset} value={String(p.offset)} text={periodOptionLabel(p, cadence, t)}>
                {periodOptionLabel(p, cadence, t)}
              </Option>
            ))}
          </Dropdown>
        </Field>
      </div>

      <div className={s.grid}>
        <Card className={s.card}>
          <CardHeader
            className={s.cardHeader}
            header={<Text weight="semibold">{t('analytics.missing.trend.title')}</Text>}
            description={<span className={s.cardSub}>{t('analytics.missing.trend.description', {
              cadence: t(`analytics.cadence.${cadence.toLowerCase()}`).toLowerCase(),
            })}</span>}
          />
          {history.isLoading ? (
            <div className={s.empty}>{t('analytics.common.loading')}</div>
          ) : trend.length === 0 ? (
            <div className={s.empty}>{t('analytics.common.noData')}</div>
          ) : (
            <ResponsiveContainer width="100%" height={260}>
              <BarChart data={trend} margin={{ top: 8, right: 16, bottom: 8, left: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} />
                <XAxis dataKey="label" tick={{ fontSize: 11 }} interval={0} angle={-30} textAnchor="end" height={56} />
                <YAxis allowDecimals={false} tick={{ fontSize: 11 }} />
                <Tooltip
                  cursor={{ fill: tokens.colorNeutralBackground1Hover }}
                  formatter={(v) => [v, t('analytics.missing.chart.missing')]}
                />
                <Bar
                  dataKey="missing"
                  radius={[3, 3, 0, 0]}
                  cursor="pointer"
                  onClick={(d) => {
                    const o = (d as { payload?: { offset?: number } }).payload?.offset
                    if (typeof o === 'number') setOffset(o)
                  }}
                >
                  {trend.map((row) => (
                    <Cell
                      key={row.offset}
                      // Current window = amber (still open), closed windows = red. The selected
                      // window is fully opaque; the rest are dimmed so the choice stands out.
                      fill={row.offset === 0 ? tokens.colorStatusWarningForeground1 : tokens.colorStatusDangerForeground1}
                      fillOpacity={row.offset === offset ? 1 : 0.45}
                    />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          )}
        </Card>

        <Card className={s.card}>
          <CardHeader
            className={s.cardHeader}
            header={<Text weight="semibold">{t('analytics.missing.byService.title', {
              period: periodText || t(`analytics.cadence.${cadence.toLowerCase()}`),
            })}</Text>}
            description={<span className={s.cardSub}>{t('analytics.missing.byService.description')}</span>}
          />
          {period.isLoading ? (
            <div className={s.empty}>{t('analytics.common.loading')}</div>
          ) : byService.length === 0 ? (
            <div className={s.empty}>{t('analytics.missing.byService.empty')}</div>
          ) : (
            <ResponsiveContainer width="100%" height={Math.max(180, byService.length * 34 + 40)}>
              <BarChart data={byService} layout="vertical" margin={{ top: 8, right: 24, bottom: 8, left: 8 }}>
                <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} horizontal={false} />
                <XAxis type="number" allowDecimals={false} tick={{ fontSize: 11 }} />
                <YAxis type="category" dataKey="name" width={140} tick={{ fontSize: 11 }} />
                <Tooltip cursor={{ fill: tokens.colorNeutralBackground1Hover }} formatter={(v) => [v, t('analytics.missing.chart.missing')]} />
                <Bar dataKey="missing" fill={tokens.colorStatusDangerForeground1} radius={[0, 3, 3, 0]} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </Card>
      </div>

      <Card className={s.card}>
        <CardHeader
          className={s.cardHeader}
          header={
            <div className={s.totals}>
              <span className={s.bigNumber}>{totalMissing}</span>
              <Text className={s.cardSub}>{t('analytics.missing.summary', {
                missing: totalMissing,
                services: byService.length,
                period: periodText || t('analytics.missing.selectedPeriod'),
              })}</Text>
            </div>
          }
        />
        <PeriodDetailTable loading={period.isLoading} entries={period.data?.entries ?? []} styles={s} />
      </Card>
    </div>
  )
}

function PeriodDetailTable({
  loading,
  entries,
  styles,
}: {
  loading: boolean
  entries: MissingSubmissionEntry[]
  styles: ReturnType<typeof useStyles>
}) {
  const { t } = useTranslation()
  if (loading) return <div className={styles.empty}>{t('analytics.common.loading')}</div>
  if (entries.length === 0) return <MessageBar intent="success"><MessageBarBody>{t('analytics.missing.detail.complete')}</MessageBarBody></MessageBar>

  return (
    <Table size="small">
      <TableHeader>
        <TableRow>
          <TableHeaderCell>{t('analytics.common.service')}</TableHeaderCell>
          <TableHeaderCell>{t('analytics.common.schema')}</TableHeaderCell>
          <TableHeaderCell>{t('analytics.missing.columns.missing')}</TableHeaderCell>
        </TableRow>
      </TableHeader>
      <TableBody>
        {entries.map((e) => (
          <TableRow key={`${e.serviceId}-${e.schemaName}`}>
            <TableCell>
              <TableCellLayout>
                <Link to={`/services/${encodeURIComponent(e.serviceName)}/status`} className={styles.link}>
                  {e.serviceLabel || e.serviceName}
                </Link>
              </TableCellLayout>
            </TableCell>
            <TableCell>{e.schemaLabel || e.schemaName}</TableCell>
            <TableCell>
              <Badge appearance="outline" color="danger">
                {e.missingRequiredCount}/{e.totalRequiredCount}
              </Badge>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

/** Compact X-axis label for a window, formatted by cadence (mirrors the schema history chart). */
function periodLabel(periodStart: string, cadence: Cadence): string {
  const start = new Date(periodStart)
  if (Number.isNaN(start.getTime())) return periodStart
  const y = start.getUTCFullYear()
  const m = start.getUTCMonth() + 1
  const d = start.getUTCDate()
  switch (cadence) {
    case 'Daily':
      return `${y}-${pad(m)}-${pad(d)}`
    case 'Weekly':
    case 'Fortnightly':
      return `${pad(m)}-${pad(d)}`
    case 'Monthly':
      return `${y}-${pad(m)}`
    case 'Quarterly':
      return `${y}-Q${Math.floor((m - 1) / 3) + 1}`
    case 'SemiAnnually':
      return `${y}-${m <= 6 ? 'H1' : 'H2'}`
    case 'Yearly':
      return `${y}`
    default:
      return start.toISOString()
  }
}

/** Longer dropdown label that adds a "current"/"previous" hint on the two most recent windows. */
function periodOptionLabel(point: MissingHistoryPoint, cadence: Cadence, t: TFunction): string {
  const base = periodLabel(point.periodStart, cadence)
  if (point.offset === 0) return `${base} (${t('analytics.explore.filters.current')})`
  if (point.offset === -1) return `${base} (${t('analytics.explore.trend.previous')})`
  return base
}

function pad(n: number): string {
  return n < 10 ? `0${n}` : String(n)
}
