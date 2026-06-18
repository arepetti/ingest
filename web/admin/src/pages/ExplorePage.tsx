import { useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  Card, CardHeader, Dropdown, MessageBar, MessageBarBody, Option, Switch,
  Tab, TabList, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Text, Title2, Tooltip as FluentTooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  ArrowClockwise20Regular, ArrowDownload20Regular, Image20Regular, Info16Regular, MoreHorizontal20Regular,
} from '@fluentui/react-icons'
import {
  Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { PeriodFilter } from '../components/PeriodFilter'
import { formatApiError } from '../api/client'
import { useAccounts, useExploreSeries, useSchemas } from '../api/hooks'
import type {
  Account, ExploreAggregation, ExploreServicePoint, ExploreValueSeries, Schema, SchemaValue,
} from '../api/types'
import { addCadence, cadenceLabel } from '../utils/cadence'
import { formatPeriodLabel } from '../utils/periodFormat'
import { intervalRange, shiftIso, SHIFT_LABELS, type Interval, type ShiftKey } from '../utils/period'
import { ExplorePresets } from '../components/ExplorePresets'
import type { PeriodFilterState } from '../utils/usePeriodFilter'
import { buildCsv } from '../utils/csv'
import { downloadText } from '../utils/download'
import { exportChartPng } from '../utils/chartExport'

type ViewKind = 'trend' | 'compare' | 'snapshot'

// How many future periods the optional projection extends the trend chart by.
const PROJECTION_PERIODS = 2

const SHIFTS: ShiftKey[] = ['1m', '6m', '1y']

const AGGREGATIONS: ExploreAggregation[] = ['Average', 'Sum', 'Min', 'Max', 'Count']
const AGG_LABELS: Record<ExploreAggregation, string> = {
  Average: 'Average', Sum: 'Sum', Min: 'Minimum', Max: 'Maximum', Count: 'Sample count',
}

// A small categorical palette that reads acceptably in both light and dark themes. Cycled when
// there are more services than colours.
const SERIES_COLORS = [
  '#2563eb', '#dc2626', '#059669', '#d97706', '#7c3aed',
  '#0891b2', '#db2777', '#65a30d', '#475569', '#c026d3',
]

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  filters: { display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' },
  // Mirror the small, muted filter labels used by the other PeriodFilter pages (e.g. AuditPage)
  // so the bundled "Period" label lines up with the rest of the row.
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  fieldLabel: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3, display: 'flex', alignItems: 'center', gap: '4px' },
  infoIcon: { color: tokens.colorNeutralForeground3, cursor: 'help' },
  dropdown: { minWidth: '180px' },
  statRow: { display: 'flex', gap: '12px', flexWrap: 'wrap' },
  stat: { padding: '12px 16px', minWidth: '120px' },
  statLabel: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  statValue: { fontSize: '24px', fontWeight: 700 },
  card: { padding: '16px' },
  cardHeader: { marginBottom: '8px' },
  cardHeaderRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  cardSub: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  empty: { color: tokens.colorNeutralForeground3, padding: '32px 0', textAlign: 'center' },
  chartWrap: { width: '100%' },
  tableScroll: { overflowX: 'auto' },
  numCell: { textAlign: 'right', fontVariantNumeric: 'tabular-nums' },
})

export function ExplorePage() {
  const s = useStyles()
  const [sp, setSp] = useSearchParams()

  // URL is the single source of truth so a filtered view is shareable by copying the address bar.
  const update = (patch: Record<string, string | null>) => {
    setSp(prev => {
      const next = new URLSearchParams(prev)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') next.delete(k)
        else next.set(k, v)
      }
      return next
    }, { replace: true })
  }

  const schemas = useSchemas()
  const schemaList = useMemo(
    () => [...(schemas.data?.items ?? [])].sort((a, b) => label(a).localeCompare(label(b))),
    [schemas.data],
  )

  // Resolve the active schema: the URL value when valid, otherwise the first one available.
  const schemaParam = sp.get('schema') ?? ''
  const schema: Schema | undefined =
    schemaList.find(x => x.name === schemaParam) ?? schemaList[0]
  const schemaName = schema?.name ?? ''

  const numericValues: SchemaValue[] = useMemo(
    () => (schema?.values ?? []).filter(v => v.type === 'Number' || v.type === 'Integer'),
    [schema],
  )
  const valueParam = sp.get('value') ?? ''
  const activeValueName =
    numericValues.find(v => v.name === valueParam)?.name ?? numericValues[0]?.name ?? ''

  const agg = (sp.get('agg') as ExploreAggregation) || 'Average'
  const view = (sp.get('view') as ViewKind) || 'trend'
  const combined = sp.get('combined') === '1'
  const asTable = sp.get('table') === '1'
  const projecting = sp.get('proj') === '1'
  // A single "Compare with previous" dropdown: empty/absent means off, otherwise it's the shift.
  const shift = (sp.get('shift') ?? '') as ShiftKey | ''
  const comparing = shift !== ''

  // Period filter backed by the URL so it round-trips with everything else.
  const interval = (sp.get('period') as Interval) || 'all'
  const customFrom = sp.get('cfrom') ?? ''
  const customTo = sp.get('cto') ?? ''
  const { from, to } = intervalRange(interval, customFrom, customTo)

  // "Compare with previous" only makes sense for a bounded window, so it needs both ends resolved.
  const canCompare = !!from && !!to
  const from2 = comparing && canCompare ? shiftIso(from!, shift as ShiftKey) : undefined
  const to2 = comparing && canCompare ? shiftIso(to!, shift as ShiftKey) : undefined
  const periodState: PeriodFilterState = {
    interval,
    setInterval: v => update({ period: v === 'all' ? null : v }),
    customFrom, setCustomFrom: v => update({ cfrom: v }),
    customTo, setCustomTo: v => update({ cto: v }),
    from, to,
  }

  // Service multiselect options come from the account registry (independent of the data), so the
  // filter is usable even before any series loads.
  const accounts = useAccounts({ role: 'Service', pageSize: 500 })
  const serviceAccounts: Account[] = useMemo(
    () => [...(accounts.data?.items ?? [])].sort((a, b) => label(a).localeCompare(label(b))),
    [accounts.data],
  )
  const selectedServiceIds = useMemo(
    () => (sp.get('services') ?? '').split(',').filter(Boolean),
    [sp],
  )
  const toggleService = (id: string) => {
    const set = new Set(selectedServiceIds)
    if (set.has(id)) set.delete(id)
    else set.add(id)
    update({ services: [...set].join(',') })
  }

  const series = useExploreSeries(
    {
      schema: schemaName,
      serviceIds: selectedServiceIds.length ? selectedServiceIds : undefined,
      from, to, agg,
    },
    !!schemaName,
  )

  // Previous-period overlay: the same query shifted back by `shift`. Only fetched for the Trend
  // view, when the toggle is on and the window is bounded.
  const compareEnabled = comparing && canCompare && view === 'trend'
  const prevSeries = useExploreSeries(
    {
      schema: schemaName,
      serviceIds: selectedServiceIds.length ? selectedServiceIds : undefined,
      from: from2, to: to2, agg,
    },
    !!schemaName && compareEnabled,
  )

  const activeSeries: ExploreValueSeries | undefined =
    series.data?.values.find(v => v.valueName === activeValueName)
  const prevActiveSeries: ExploreValueSeries | undefined =
    compareEnabled ? prevSeries.data?.values.find(v => v.valueName === activeValueName) : undefined
  const seriesServices = series.data?.services ?? []

  const chartRef = useRef<HTMLDivElement>(null)
  const [exportError, setExportError] = useState<string | null>(null)

  const exportCsv = () => {
    try {
      if (!series.data) return
      const { headers, rows, name } = buildExportRows(view, series.data.values, activeSeries, seriesServices, agg)
      if (rows.length === 0) { setExportError('Nothing to export for this view yet.'); return }
      downloadText(`explore-${schemaName}-${name}.csv`, buildCsv(headers, rows), 'text/csv;charset=utf-8')
    } catch (e) {
      setExportError(formatApiError(e))
    }
  }

  const exportPng = async () => {
    try {
      await exportChartPng(chartRef.current, `explore-${schemaName}-${view}.png`)
    } catch (e) {
      setExportError(formatApiError(e))
    }
  }

  const noNumeric = !!schema && numericValues.length === 0

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Title2>Explore</Title2>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <ExplorePresets
            current={sp.toString()}
            onLoad={q => setSp(new URLSearchParams(q), { replace: true })}
          />
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => series.refetch()}>Refresh</MenuItem>
                <MenuDivider />
                <MenuItem icon={<ArrowDownload20Regular />} disabled={!series.data} onClick={exportCsv}>Export CSV (this view)</MenuItem>
                {view !== 'snapshot' && (
                  <MenuItem icon={<Image20Regular />} disabled={!series.data} onClick={exportPng}>Export chart (PNG)</MenuItem>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      </div>

      {(schemas.error || series.error) && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{formatApiError(schemas.error || series.error)}</MessageBarBody>
        </AutoScrollMessageBar>
      )}
      {exportError && (
        <AutoScrollMessageBar intent="error"><MessageBarBody>{exportError}</MessageBarBody></AutoScrollMessageBar>
      )}

      <div className={s.filters}>
        <div className={s.field}>
          <span className={s.fieldLabel}>Schema</span>
          <Dropdown
            className={s.dropdown}
            size="small"
            selectedOptions={schemaName ? [schemaName] : []}
            value={schema ? label(schema) : ''}
            placeholder="Select a schema"
            onOptionSelect={(_, d) => update({ schema: d.optionValue ?? null, value: null })}
          >
            {schemaList.map(x => <Option key={x.name} value={x.name}>{label(x)}</Option>)}
          </Dropdown>
        </div>

        {view !== 'snapshot' && (
          <div className={s.field}>
            <span className={s.fieldLabel}>Value</span>
            <Dropdown
              className={s.dropdown}
              size="small"
              selectedOptions={activeValueName ? [activeValueName] : []}
              value={numericValues.find(v => v.name === activeValueName)?.label || activeValueName}
              placeholder="Select a value"
              disabled={numericValues.length === 0}
              onOptionSelect={(_, d) => update({ value: d.optionValue ?? null })}
            >
              {numericValues.map(v => <Option key={v.name} value={v.name}>{v.label || v.name}</Option>)}
            </Dropdown>
          </div>
        )}

        <div className={s.field}>
          <span className={s.fieldLabel}>Services</span>
          <Dropdown
            className={s.dropdown}
            size="small"
            multiselect
            selectedOptions={selectedServiceIds}
            value={selectedServiceIds.length === 0 ? 'All services' : `${selectedServiceIds.length} selected`}
            placeholder="All services"
            onOptionSelect={(_, d) => d.optionValue && toggleService(d.optionValue)}
          >
            {serviceAccounts.map(a => <Option key={a.id} value={a.id}>{label(a)}</Option>)}
          </Dropdown>
        </div>

        <div className={s.field}>
          <span className={s.fieldLabel}>
            Aggregation
            <FluentTooltip
              relationship="description"
              content="How the samples that fall in each period are reduced to one number — and how several services are combined into the overall figure. Average is count-weighted; Sample count just tallies how many were submitted."
            >
              <Info16Regular className={s.infoIcon} tabIndex={0} aria-label="What does Aggregation do?" />
            </FluentTooltip>
          </span>
          <Dropdown
            className={s.dropdown}
            size="small"
            selectedOptions={[agg]}
            value={AGG_LABELS[agg]}
            onOptionSelect={(_, d) => update({ agg: (d.optionValue as ExploreAggregation) ?? null })}
          >
            {AGGREGATIONS.map(a => <Option key={a} value={a}>{AGG_LABELS[a]}</Option>)}
          </Dropdown>
        </div>

      </div>

      <div className={s.filters}>
        <PeriodFilter state={periodState} />

        {view === 'trend' && (
          <div className={s.field}>
            <span className={s.fieldLabel}>
              Compare with previous
              <FluentTooltip
                relationship="description"
                content="Overlay the same selection shifted back in time so you can read this period against an earlier one. Needs a Period range (not All time); the two windows may overlap."
              >
                <Info16Regular className={s.infoIcon} tabIndex={0} aria-label="What does Compare do?" />
              </FluentTooltip>
            </span>
            <Dropdown
              className={s.dropdown}
              size="small"
              disabled={!canCompare}
              selectedOptions={[comparing ? shift : 'off']}
              value={comparing ? SHIFT_LABELS[shift as ShiftKey] : 'No'}
              onOptionSelect={(_, d) => update({ shift: d.optionValue && d.optionValue !== 'off' ? d.optionValue : null })}
            >
              <Option value="off">No</Option>
              {SHIFTS.map(k => <Option key={k} value={k}>{SHIFT_LABELS[k]}</Option>)}
            </Dropdown>
          </div>
        )}
      </div>

      <TabList selectedValue={view} onTabSelect={(_, d) => update({ view: d.value as string })}>
        <Tab value="trend">Trend</Tab>
        <Tab value="compare">Compare services</Tab>
        <Tab value="snapshot">Snapshot</Tab>
      </TabList>

      {!schemaName ? (
        <Card className={s.card}><div className={s.empty}>Pick a schema to start exploring.</div></Card>
      ) : noNumeric ? (
        <MessageBar intent="info">
          <MessageBarBody>
            This schema has no numeric values, so there's nothing to chart. Add a Number or Integer value to explore it.
          </MessageBarBody>
        </MessageBar>
      ) : series.isLoading ? (
        <Card className={s.card}><div className={s.empty}>Loading…</div></Card>
      ) : view === 'snapshot' ? (
        <SnapshotView styles={s} values={series.data?.values ?? []} services={seriesServices} agg={agg} />
      ) : (
        <>
          <StatRow styles={s} series={activeSeries} agg={agg} serviceCount={seriesServices.length} />
          <Card className={s.card}>
            <div className={s.cardHeaderRow}>
              <CardHeader
                className={s.cardHeader}
                header={<Text weight="semibold">{view === 'trend' ? 'Trend over time' : 'Compare services'}</Text>}
                description={
                  <span className={s.cardSub}>
                    {AGG_LABELS[agg]} of {valueTitle(activeSeries, activeValueName)}
                    {activeSeries?.cadence ? ` · per ${cadenceLabel(activeSeries.cadence).toLowerCase()} period` : ''}
                  </span>
                }
              />
              <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
                {view === 'trend' && (
                  <Switch
                    label="Combine services"
                    checked={combined}
                    onChange={(_, d) => update({ combined: d.checked ? '1' : null })}
                  />
                )}
                {view === 'trend' && !asTable && (
                  <Switch
                    label="Projection"
                    checked={projecting}
                    onChange={(_, d) => update({ proj: d.checked ? '1' : null })}
                  />
                )}
                <Switch label="View as table" checked={asTable} onChange={(_, d) => update({ table: d.checked ? '1' : null })} />
              </div>
            </div>

            {!activeSeries || activeSeries.buckets.length === 0 ? (
              <div className={s.empty}>No samples for this selection.</div>
            ) : view === 'trend' ? (
              asTable
                ? <TrendTable styles={s} series={activeSeries} services={seriesServices} combined={combined} />
                : <div className={s.chartWrap} ref={chartRef}><TrendChart series={activeSeries} services={seriesServices} combined={combined} projectPeriods={projecting ? PROJECTION_PERIODS : 0} previous={prevActiveSeries} previousLabel={comparing ? SHIFT_LABELS[shift as ShiftKey] : undefined} /></div>
            ) : (
              asTable
                ? <CompareTable styles={s} series={activeSeries} services={seriesServices} agg={agg} />
                : <div className={s.chartWrap} ref={chartRef}><CompareChart series={activeSeries} services={seriesServices} agg={agg} /></div>
            )}
          </Card>
        </>
      )}
    </div>
  )
}

// --- Stat cards ---------------------------------------------------------------------------

function StatRow({ styles, series, agg, serviceCount }: {
  styles: ReturnType<typeof useStyles>
  series: ExploreValueSeries | undefined
  agg: ExploreAggregation
  serviceCount: number
}) {
  if (!series || series.buckets.length === 0) return null
  const totalSamples = series.buckets.reduce((acc, b) => acc + b.count, 0)
  const latest = series.buckets[series.buckets.length - 1]
  const overall = rollup(agg, series.buckets.map(b => ({ value: b.value, count: b.count })))
  const unit = series.unit ? ` ${series.unit}` : ''
  return (
    <div className={styles.statRow}>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>Overall {AGG_LABELS[agg].toLowerCase()}</span>
        <span className={styles.statValue}>{fmt(overall)}{unit}</span>
      </Card>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>Latest period</span>
        <span className={styles.statValue}>{fmt(latest.value)}{unit}</span>
      </Card>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>Samples</span>
        <span className={styles.statValue}>{totalSamples}</span>
      </Card>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>Periods / services</span>
        <span className={styles.statValue}>{series.buckets.length} / {serviceCount}</span>
      </Card>
    </div>
  )
}

// --- Trend --------------------------------------------------------------------------------

type TrendRow = { period: string } & Record<string, number | string>

function trendRows(series: ExploreValueSeries, combined: boolean): TrendRow[] {
  return series.buckets.map(b => {
    const row: TrendRow = { period: formatPeriodLabel(b.periodStart, series.cadence) }
    if (combined) {
      row.overall = round(b.value)
    } else {
      for (const sp of b.services) row[sp.serviceId] = round(sp.value)
    }
    return row
  })
}

type SeriesDef = { key: string; name: string; color: string }

// Ordinary least-squares fit of y = slope*x + intercept. Returns null when there aren't enough
// distinct points to define a line.
function linregress(points: { x: number; y: number }[]): { slope: number; intercept: number } | null {
  const n = points.length
  if (n < 2) return null
  let sx = 0, sy = 0, sxx = 0, sxy = 0
  for (const p of points) { sx += p.x; sy += p.y; sxx += p.x * p.x; sxy += p.x * p.y }
  const denom = n * sxx - sx * sx
  if (denom === 0) return null
  const slope = (n * sxy - sx * sy) / denom
  const intercept = (sy - slope * sx) / n
  return { slope, intercept }
}

/**
 * Build the chart rows plus optional projection. When `projectPeriods > 0` (and there are at least
 * two periods to fit), the result appends that many future periods and, for each drawn line, a
 * dashed `<key>__proj` series that continues from the last real point using a linear fit. In
 * per-service mode it also adds a single straight `__trend` line for the aggregate direction.
 */
function buildTrend(
  series: ExploreValueSeries,
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[],
  combined: boolean,
  projectPeriods: number,
  previous?: ExploreValueSeries,
): { rows: TrendRow[]; defs: SeriesDef[]; projected: boolean; overallTrend: boolean; compared: boolean } {
  const buckets = series.buckets
  const n = buckets.length
  const defs: SeriesDef[] = combined
    ? [{ key: 'overall', name: 'All services', color: SERIES_COLORS[0] }]
    : services.map((svc, i) => ({
        key: svc.serviceId,
        name: svc.serviceLabel || svc.serviceName,
        color: SERIES_COLORS[i % SERIES_COLORS.length],
      }))

  const valueAt = (b: typeof buckets[number], key: string): number | undefined =>
    key === 'overall' ? b.value : b.services.find(p => p.serviceId === key)?.value

  const rows: TrendRow[] = buckets.map(b => {
    const row: TrendRow = { period: formatPeriodLabel(b.periodStart, series.cadence) }
    for (const def of defs) {
      const v = valueAt(b, def.key)
      if (v !== undefined) row[def.key] = round(v)
    }
    return row
  })

  // Previous-period overlay: align the shifted buckets to the current ones by index, so the same
  // ordinal period (week 1 vs week 1, …) lines up on the shared x-axis.
  const prevBuckets = previous?.buckets ?? []
  const compared = prevBuckets.length > 0
  if (compared) {
    for (let i = 0; i < rows.length && i < prevBuckets.length; i++) {
      for (const def of defs) {
        const v = valueAt(prevBuckets[i], def.key)
        if (v !== undefined) rows[i][`${def.key}__prev`] = round(v)
      }
    }
  }

  if (projectPeriods <= 0 || n < 2) return { rows, defs, projected: false, overallTrend: false, compared }

  let cursor: string | Date = buckets[n - 1].periodStart
  for (let j = 0; j < projectPeriods; j++) {
    cursor = addCadence(cursor, series.cadence)
    rows.push({ period: formatPeriodLabel(cursor.toISOString(), series.cadence) })
  }

  for (const def of defs) {
    const pts: { x: number; y: number }[] = []
    buckets.forEach((b, i) => { const v = valueAt(b, def.key); if (v !== undefined) pts.push({ x: i, y: v }) })
    const reg = linregress(pts)
    if (!reg) continue
    // Anchor the dashed line at the last real value so it visually continues the solid line.
    const lastVal = valueAt(buckets[n - 1], def.key)
    if (lastVal !== undefined) rows[n - 1][`${def.key}__proj`] = round(lastVal)
    for (let j = 0; j < projectPeriods; j++) {
      const idx = n + j
      rows[idx][`${def.key}__proj`] = round(reg.intercept + reg.slope * idx)
    }
  }

  // A single straight aggregate trendline. Skipped in combined mode where the one projected line
  // already conveys the overall direction.
  let overallTrend = false
  if (!combined) {
    const reg = linregress(buckets.map((b, i) => ({ x: i, y: b.value })))
    if (reg) {
      overallTrend = true
      for (let k = 0; k < rows.length; k++) rows[k].__trend = round(reg.intercept + reg.slope * k)
    }
  }

  return { rows, defs, projected: true, overallTrend, compared }
}

function TrendChart({ series, services, combined, projectPeriods, previous, previousLabel }: {
  series: ExploreValueSeries
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[]
  combined: boolean
  projectPeriods: number
  previous?: ExploreValueSeries
  previousLabel?: string
}) {
  const { rows, defs, projected, overallTrend, compared } = useMemo(
    () => buildTrend(series, services, combined, projectPeriods, previous),
    [series, services, combined, projectPeriods, previous],
  )
  const prevSuffix = previousLabel ? ` (${previousLabel} ago)` : ' (previous)'
  const showLegend = (!combined && defs.length > 1) || (projected && overallTrend) || compared
  return (
    <ResponsiveContainer width="100%" height={340}>
      <LineChart data={rows} margin={{ top: 8, right: 24, bottom: 8, left: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} />
        <XAxis dataKey="period" tick={{ fontSize: 11 }} interval="preserveStartEnd" angle={-30} textAnchor="end" height={56} />
        <YAxis tick={{ fontSize: 11 }} />
        <Tooltip />
        {showLegend && <Legend wrapperStyle={{ fontSize: 12 }} />}
        {defs.map(def => (
          <Line
            key={def.key}
            type="monotone"
            dataKey={def.key}
            name={def.name}
            stroke={def.color}
            strokeWidth={2}
            dot={{ r: 2 }}
            connectNulls
          />
        ))}
        {compared && defs.map(def => (
          <Line
            key={`${def.key}__prev`}
            type="monotone"
            dataKey={`${def.key}__prev`}
            name={`${def.name}${prevSuffix}`}
            stroke={def.color}
            strokeOpacity={0.55}
            strokeWidth={2}
            strokeDasharray="4 4"
            dot={false}
            connectNulls
          />
        ))}
        {projected && defs.map(def => (
          <Line
            key={`${def.key}__proj`}
            type="linear"
            dataKey={`${def.key}__proj`}
            name={`${def.name} (projection)`}
            stroke={def.color}
            strokeWidth={2}
            strokeDasharray="5 5"
            dot={false}
            connectNulls
            legendType="none"
          />
        ))}
        {projected && overallTrend && (
          <Line
            type="linear"
            dataKey="__trend"
            name="Overall trend"
            stroke={tokens.colorNeutralForeground2}
            strokeWidth={2}
            strokeDasharray="6 4"
            dot={false}
            connectNulls
          />
        )}
      </LineChart>
    </ResponsiveContainer>
  )
}

function TrendTable({ styles, series, services, combined }: {
  styles: ReturnType<typeof useStyles>
  series: ExploreValueSeries
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[]
  combined: boolean
}) {
  const rows = trendRows(series, combined)
  const cols = combined ? [{ key: 'overall', name: 'All services' }] : services.map(s => ({ key: s.serviceId, name: s.serviceLabel || s.serviceName }))
  return (
    <div className={styles.tableScroll}>
      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Period</TableHeaderCell>
            {cols.map(c => <TableHeaderCell key={c.key}>{c.name}</TableHeaderCell>)}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map(r => (
            <TableRow key={r.period}>
              <TableCell>{r.period}</TableCell>
              {cols.map(c => <TableCell key={c.key} className={styles.numCell}>{cell(r[c.key])}</TableCell>)}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

// --- Compare ------------------------------------------------------------------------------

type CompareRow = { name: string; value: number }

function compareRows(
  series: ExploreValueSeries,
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[],
  agg: ExploreAggregation,
): CompareRow[] {
  return services
    .map(svc => {
      const points: ExploreServicePoint[] = series.buckets
        .map(b => b.services.find(p => p.serviceId === svc.serviceId))
        .filter((p): p is ExploreServicePoint => !!p)
      return { name: svc.serviceLabel || svc.serviceName, value: round(rollup(agg, points)) }
    })
    .sort((a, b) => b.value - a.value)
}

function CompareChart({ series, services, agg }: {
  series: ExploreValueSeries
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[]
  agg: ExploreAggregation
}) {
  const rows = useMemo(() => compareRows(series, services, agg), [series, services, agg])
  return (
    <ResponsiveContainer width="100%" height={Math.max(220, rows.length * 34 + 48)}>
      <BarChart data={rows} layout="vertical" margin={{ top: 8, right: 32, bottom: 8, left: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} horizontal={false} />
        <XAxis type="number" tick={{ fontSize: 11 }} />
        <YAxis type="category" dataKey="name" width={160} tick={{ fontSize: 11 }} />
        <Tooltip cursor={{ fill: tokens.colorNeutralBackground1Hover }} />
        <Bar dataKey="value" name={AGG_LABELS[agg]} fill={SERIES_COLORS[0]} radius={[0, 3, 3, 0]} />
      </BarChart>
    </ResponsiveContainer>
  )
}

function CompareTable({ styles, series, services, agg }: {
  styles: ReturnType<typeof useStyles>
  series: ExploreValueSeries
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[]
  agg: ExploreAggregation
}) {
  const rows = compareRows(series, services, agg)
  return (
    <div className={styles.tableScroll}>
      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Service</TableHeaderCell>
            <TableHeaderCell>{AGG_LABELS[agg]}</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map(r => (
            <TableRow key={r.name}>
              <TableCell>{r.name}</TableCell>
              <TableCell className={styles.numCell}>{fmt(r.value)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

// --- Snapshot -----------------------------------------------------------------------------

function SnapshotView({ styles, values, services, agg }: {
  styles: ReturnType<typeof useStyles>
  values: ExploreValueSeries[]
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[]
  agg: ExploreAggregation
}) {
  if (values.length === 0 || services.length === 0) {
    return <Card className={styles.card}><div className={styles.empty}>No samples for this selection.</div></Card>
  }
  // Latest bucket per value, indexed by service for O(1) cell lookup.
  const latestByValue = values.map(v => {
    const latest = v.buckets[v.buckets.length - 1]
    const byService = new Map<string, number>()
    if (latest) for (const p of latest.services) byService.set(p.serviceId, p.value)
    return { value: v, period: latest ? formatPeriodLabel(latest.periodStart, v.cadence) : '—', byService }
  })

  return (
    <Card className={styles.card}>
      <CardHeader
        className={styles.cardHeader}
        header={<Text weight="semibold">Latest value per service</Text>}
        description={
          <span className={styles.cardSub}>
            Most recent period per value · cells show the {AGG_LABELS[agg].toLowerCase()} for that period
          </span>
        }
      />
      <div className={styles.tableScroll}>
        <Table size="small">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Service</TableHeaderCell>
              {latestByValue.map(c => (
                <TableHeaderCell key={c.value.valueName}>
                  <div style={{ display: 'flex', flexDirection: 'column' }}>
                    <span>{c.value.label || c.value.valueName}{c.value.unit ? ` (${c.value.unit})` : ''}</span>
                    <span className={styles.cardSub}>{c.period}</span>
                  </div>
                </TableHeaderCell>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {services.map(svc => (
              <TableRow key={svc.serviceId}>
                <TableCell>{svc.serviceLabel || svc.serviceName}</TableCell>
                {latestByValue.map(c => {
                  const v = c.byService.get(svc.serviceId)
                  return <TableCell key={c.value.valueName} className={styles.numCell}>{v === undefined ? '—' : fmt(v)}</TableCell>
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </Card>
  )
}

// --- Shared helpers -----------------------------------------------------------------------

/** Reduce a set of per-bucket points to one figure for the whole range, matching the aggregation. */
function rollup(agg: ExploreAggregation, points: { value: number; count: number }[]): number {
  if (points.length === 0) return 0
  switch (agg) {
    case 'Sum':
    case 'Count':
      return points.reduce((acc, p) => acc + p.value, 0)
    case 'Min':
      return Math.min(...points.map(p => p.value))
    case 'Max':
      return Math.max(...points.map(p => p.value))
    default: {
      // Exact overall mean: each bucket value is the mean of `count` samples, so weight by count.
      const totalCount = points.reduce((acc, p) => acc + p.count, 0)
      if (totalCount === 0) return points.reduce((acc, p) => acc + p.value, 0) / points.length
      return points.reduce((acc, p) => acc + p.value * p.count, 0) / totalCount
    }
  }
}

function buildExportRows(
  view: ViewKind,
  values: ExploreValueSeries[],
  activeSeries: ExploreValueSeries | undefined,
  services: { serviceId: string; serviceName: string; serviceLabel?: string | null }[],
  agg: ExploreAggregation,
): { headers: string[]; rows: (string | number)[][]; name: string } {
  if (view === 'snapshot') {
    const headers = ['Service', ...values.map(v => `${v.label || v.valueName}${v.unit ? ` (${v.unit})` : ''}`)]
    const latest = values.map(v => {
      const b = v.buckets[v.buckets.length - 1]
      const m = new Map<string, number>()
      if (b) for (const p of b.services) m.set(p.serviceId, p.value)
      return m
    })
    const rows = services.map(svc => [
      svc.serviceLabel || svc.serviceName,
      ...latest.map(m => { const x = m.get(svc.serviceId); return x === undefined ? '' : round(x) }),
    ])
    return { headers, rows, name: 'snapshot' }
  }
  if (!activeSeries) return { headers: [], rows: [], name: view }
  if (view === 'compare') {
    const headers = ['Service', AGG_LABELS[agg]]
    const rows = compareRows(activeSeries, services, agg).map(r => [r.name, r.value])
    return { headers, rows, name: `compare-${activeSeries.valueName}` }
  }
  // trend
  const headers = ['Period', ...services.map(s => s.serviceLabel || s.serviceName)]
  const rows = activeSeries.buckets.map(b => {
    const byService = new Map(b.services.map(p => [p.serviceId, p.value]))
    return [
      formatPeriodLabel(b.periodStart, activeSeries.cadence),
      ...services.map(s => { const x = byService.get(s.serviceId); return x === undefined ? '' : round(x) }),
    ]
  })
  return { headers, rows, name: `trend-${activeSeries.valueName}` }
}

function valueTitle(series: ExploreValueSeries | undefined, fallback: string): string {
  if (!series) return fallback
  return (series.label || series.valueName) + (series.unit ? ` (${series.unit})` : '')
}

function label(x: { label?: string | null; name: string }): string {
  return x.label || x.name
}

function round(n: number): number {
  return Math.round(n * 1000) / 1000
}

function fmt(n: number): string {
  if (!Number.isFinite(n)) return '—'
  return Number.isInteger(n) ? String(n) : String(Math.round(n * 100) / 100)
}

function cell(v: number | string | undefined): string {
  if (v === undefined || v === '') return '—'
  return typeof v === 'number' ? fmt(v) : v
}
