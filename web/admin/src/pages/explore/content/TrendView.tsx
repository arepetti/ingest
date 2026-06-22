import { useMemo, type RefObject } from 'react'
import {
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, tokens,
} from '@fluentui/react-components'
import {
  CartesianGrid, Legend, Line, LineChart, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import type { ExploreValueSeries, SchemaValue } from '../../../api/types'
import { addCadence } from '../../../utils/cadence'
import { formatPeriodLabel } from '../../../utils/periodFormat'
import { ragBandRects } from '../../../utils/targetBand'
import { cell, round, SERIES_COLORS, useExploreStyles, type ServiceRef } from '../shared'

// How many future periods the optional projection extends the trend chart by.
const PROJECTION_PERIODS = 2

type TrendRow = { period: string } & Record<string, number | string>
type SeriesDef = { key: string; name: string; color: string }

/** The Trend sub-view: a per-period line chart (with optional projection/compare/RAG band) or its table form. */
export function TrendView({ series, services, combined, projecting, previous, previousLabel, band, asTable, chartRef }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  combined: boolean
  projecting: boolean
  previous?: ExploreValueSeries
  previousLabel?: string
  band?: SchemaValue
  asTable: boolean
  chartRef: RefObject<HTMLDivElement | null>
}) {
  const styles = useExploreStyles()
  if (asTable) return <TrendTable series={series} services={services} combined={combined} />
  return (
    <div className={styles.chartWrap} ref={chartRef}>
      <TrendChart
        series={series}
        services={services}
        combined={combined}
        projectPeriods={projecting ? PROJECTION_PERIODS : 0}
        previous={previous}
        previousLabel={previousLabel}
        band={band}
      />
    </div>
  )
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

function TrendChart({ series, services, combined, projectPeriods, previous, previousLabel, band }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  combined: boolean
  projectPeriods: number
  previous?: ExploreValueSeries
  previousLabel?: string
  band?: SchemaValue
}) {
  const bandRects = useMemo(() => (band ? ragBandRects(band) : []), [band])
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

function TrendTable({ series, services, combined }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  combined: boolean
}) {
  const styles = useExploreStyles()
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
