import { useMemo, type RefObject } from 'react'
import {
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Text, tokens,
} from '@fluentui/react-components'
import {
  CartesianGrid, Legend, Line, LineChart, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import type { ExploreValueSeries, SchemaValue } from '../../../api/types'
import { addCadence } from '../../../utils/cadence'
import { formatPeriodLabel } from '../../../utils/periodFormat'
import { ragBandRects } from '../../../utils/targetBand'
import { ANOMALY_COLORS, cell, fmt, round, SERIES_COLORS, useExploreStyles, type ServiceRef } from '../shared'

// How many future periods the optional projection extends the trend chart by.
const PROJECTION_PERIODS = 2

type TrendRow = { period: string } & Record<string, number | string | boolean>
type SeriesDef = { key: string; name: string; color: string }

/** The Trend sub-view: a per-period line chart (with optional projection/compare/RAG band) or its table form. */
export function TrendView({ series, services, combined, projecting, previous, previousLabel, band, anomaly, asTable, chartRef }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  combined: boolean
  projecting: boolean
  previous?: ExploreValueSeries
  previousLabel?: string
  band?: SchemaValue
  anomaly: boolean
  asTable: boolean
  chartRef: RefObject<HTMLDivElement | null>
}) {
  const styles = useExploreStyles()
  const anomalyCount = useMemo(
    () => (anomaly ? countAnomalies(series, combined) : 0),
    [anomaly, series, combined],
  )
  if (asTable) return <TrendTable series={series} services={services} combined={combined} anomaly={anomaly} />
  return (
    <div className={styles.chartWrap} ref={chartRef}>
      {anomaly && (
        <Text size={200} style={{ color: tokens.colorNeutralForeground3, display: 'block', marginBottom: 4 }}>
          {anomalyCount === 0
            ? 'No anomalies in the current selection.'
            : `${anomalyCount} ${anomalyCount === 1 ? 'anomaly' : 'anomalies'} highlighted in the current selection.`}
        </Text>
      )}
      <TrendChart
        series={series}
        services={services}
        combined={combined}
        projectPeriods={projecting ? PROJECTION_PERIODS : 0}
        previous={previous}
        previousLabel={previousLabel}
        band={band}
        anomaly={anomaly}
      />
    </div>
  )
}

/** Count flagged points across the drawn lines (the overall line when combined, else per service). */
function countAnomalies(series: ExploreValueSeries, combined: boolean): number {
  if (combined) return series.buckets.filter(b => b.isAnomaly).length
  return series.buckets.reduce((acc, b) => acc + b.services.filter(p => p.isAnomaly).length, 0)
}

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
  services: ServiceRef[],
  combined: boolean,
  projectPeriods: number,
  previous?: ExploreValueSeries,
  anomaly: boolean = false,
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
  // Anomaly flag / score for a line at a bucket: the overall fields when combined, else the service point's.
  const anomAt = (b: typeof buckets[number], key: string): { flag: boolean; z: number | null } => {
    if (key === 'overall') return { flag: !!b.isAnomaly, z: b.z ?? null }
    const p = b.services.find(sp => sp.serviceId === key)
    return { flag: !!p?.isAnomaly, z: p?.z ?? null }
  }

  const rows: TrendRow[] = buckets.map(b => {
    const row: TrendRow = { period: formatPeriodLabel(b.periodStart, series.cadence) }
    for (const def of defs) {
      const v = valueAt(b, def.key)
      if (v !== undefined) row[def.key] = round(v)
      if (anomaly && v !== undefined) {
        const a = anomAt(b, def.key)
        row[`${def.key}__az`] = a.flag
        if (a.z !== null) row[`${def.key}__z`] = round(a.z)
      }
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

function TrendChart({ series, services, combined, projectPeriods, previous, previousLabel, band, anomaly }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  combined: boolean
  projectPeriods: number
  previous?: ExploreValueSeries
  previousLabel?: string
  band?: SchemaValue
  anomaly: boolean
}) {
  const bandRects = useMemo(() => (band ? ragBandRects(band) : []), [band])
  const { rows, defs, projected, overallTrend, compared } = useMemo(
    () => buildTrend(series, services, combined, projectPeriods, previous, anomaly),
    [series, services, combined, projectPeriods, previous, anomaly],
  )
  const prevSuffix = previousLabel ? ` (${previousLabel} ago)` : ' (previous)'
  const showLegend = (!combined && defs.length > 1) || (projected && overallTrend) || compared
  return (
    <ResponsiveContainer width="100%" height={340}>
      <LineChart data={rows} margin={{ top: 8, right: 24, bottom: 8, left: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} />
        {bandRects.map(r => (
          // Open edges (undefined y1/y2) anchor to the axis extent, so half-open bands still shade.
          <ReferenceArea
            key={r.key}
            y1={r.y1}
            y2={r.y2}
            ifOverflow="extendDomain"
            fill={r.tone === 'green' ? tokens.colorPaletteGreenBackground2 : tokens.colorPaletteYellowBackground2}
            fillOpacity={0.3}
            strokeOpacity={0}
          />
        ))}
        <XAxis dataKey="period" tick={{ fontSize: 11 }} interval="preserveStartEnd" angle={-30} textAnchor="end" height={56} />
        <YAxis tick={{ fontSize: 11 }} />
        {anomaly ? <Tooltip content={<AnomalyTooltip defs={defs} />} /> : <Tooltip />}
        {showLegend && <Legend wrapperStyle={{ fontSize: 12 }} />}
        {defs.map(def => (
          <Line
            key={def.key}
            type="monotone"
            dataKey={def.key}
            name={def.name}
            stroke={def.color}
            strokeWidth={2}
            dot={anomaly ? anomalyDot(def.key, def.color) : { r: 2 }}
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

// A custom recharts dot factory: draws a hollow ring around flagged points and the usual small dot
// otherwise. `key` selects which line's anomaly flag (overall vs a service) to read from the row.
function anomalyDot(key: string, color: string) {
  return function Dot(props: { cx?: number; cy?: number; payload?: TrendRow; index?: number }) {
    const { cx, cy, payload, index } = props
    const k = `dot-${key}-${index ?? 0}`
    if (cx === undefined || cy === undefined || payload?.[key] === undefined) {
      return <g key={k} />
    }
    if (payload[`${key}__az`] === true) {
      return (
        <g key={k}>
          <circle cx={cx} cy={cy} r={6} fill="none" stroke={ANOMALY_COLORS.Anomaly} strokeWidth={2} />
          <circle cx={cx} cy={cy} r={2.5} fill={color} />
        </g>
      )
    }
    return <circle key={k} cx={cx} cy={cy} r={2} fill={color} />
  }
}

type TooltipEntry = { dataKey?: string | number; value?: number | string; color?: string; payload?: TrendRow }

// Tooltip used when anomaly highlighting is on: lists each drawn line's value plus its z-score, and
// marks flagged points. Skips the helper series (projection/compare/trend and the __az/__z fields).
function AnomalyTooltip({ active, label, payload, defs }: {
  active?: boolean
  label?: string | number
  payload?: TooltipEntry[]
  defs: SeriesDef[]
}) {
  const styles = useExploreStyles()
  if (!active || !payload || payload.length === 0) return null
  const known = new Set(defs.map(d => d.key))
  const row = payload[0]?.payload
  const entries = payload.filter(e => typeof e.dataKey === 'string' && known.has(e.dataKey))
  if (entries.length === 0) return null
  return (
    <div className={styles.tooltip}>
      <div className={styles.tooltipTitle}>{label}</div>
      {entries.map(e => {
        const key = e.dataKey as string
        const def = defs.find(d => d.key === key)
        const flagged = row?.[`${key}__az`] === true
        const z = row?.[`${key}__z`]
        return (
          <div key={key} className={styles.tooltipRow}>
            <span className={styles.scDot} style={{ backgroundColor: e.color ?? def?.color }} />
            <span>{def?.name ?? key}: <strong>{fmt(Number(e.value))}</strong></span>
            {typeof z === 'number' && (
              <span style={{ color: tokens.colorNeutralForeground3 }}>
                {' '}· z={fmt(z)}{flagged ? ' ⚠' : ''}
              </span>
            )}
          </div>
        )
      })}
    </div>
  )
}

function TrendTable({ series, services, combined, anomaly }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  combined: boolean
  anomaly: boolean
}) {
  const styles = useExploreStyles()
  const rows = trendRows(series, combined)
  const cols = combined ? [{ key: 'overall', name: 'All services' }] : services.map(s => ({ key: s.serviceId, name: s.serviceLabel || s.serviceName }))
  // Extra z / anomaly columns are only unambiguous for the single overall line (combined mode).
  const showAnomalyCols = anomaly && combined
  const zByPeriod = new Map(series.buckets.map(b => [formatPeriodLabel(b.periodStart, series.cadence), b]))
  return (
    <div className={styles.tableScroll}>
      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Period</TableHeaderCell>
            {cols.map(c => <TableHeaderCell key={c.key}>{c.name}</TableHeaderCell>)}
            {showAnomalyCols && <TableHeaderCell>z</TableHeaderCell>}
            {showAnomalyCols && <TableHeaderCell>Anomaly</TableHeaderCell>}
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map(r => {
            const b = zByPeriod.get(String(r.period))
            return (
              <TableRow key={String(r.period)}>
                <TableCell>{r.period}</TableCell>
                {cols.map(c => <TableCell key={c.key} className={styles.numCell}>{cell(r[c.key] as number | string | undefined)}</TableCell>)}
                {showAnomalyCols && <TableCell className={styles.numCell}>{b?.z === null || b?.z === undefined ? '—' : fmt(b.z)}</TableCell>}
                {showAnomalyCols && <TableCell>{b?.isAnomaly ? 'Yes' : ''}</TableCell>}
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}
