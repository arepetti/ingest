import { useMemo, type RefObject } from 'react'
import {
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, tokens,
} from '@fluentui/react-components'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useTranslation } from 'react-i18next'
import type { ExploreAggregation, ExploreValueSeries } from '../../../api/types'
import { aggregationLabel, compareRows, fmt, SERIES_COLORS, useExploreStyles, type ServiceRef } from '../shared'

/** The Compare sub-view: a horizontal bar ranking of services, or its table form. */
export function CompareView({ series, services, agg, asTable, chartRef }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  agg: ExploreAggregation
  asTable: boolean
  chartRef: RefObject<HTMLDivElement | null>
}) {
  const styles = useExploreStyles()
  if (asTable) return <CompareTable series={series} services={services} agg={agg} />
  return (
    <div className={styles.chartWrap} ref={chartRef}>
      <CompareChart series={series} services={services} agg={agg} />
    </div>
  )
}

function CompareChart({ series, services, agg }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  agg: ExploreAggregation
}) {
  const { t } = useTranslation()
  const rows = useMemo(() => compareRows(series, services, agg), [series, services, agg])
  return (
    <ResponsiveContainer width="100%" height={Math.max(220, rows.length * 34 + 48)}>
      <BarChart data={rows} layout="vertical" margin={{ top: 8, right: 32, bottom: 8, left: 8 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} horizontal={false} />
        <XAxis type="number" tick={{ fontSize: 11 }} />
        <YAxis type="category" dataKey="name" width={160} tick={{ fontSize: 11 }} />
        <Tooltip cursor={{ fill: tokens.colorNeutralBackground1Hover }} />
        <Bar dataKey="value" name={aggregationLabel(agg, t)} fill={SERIES_COLORS[0]} radius={[0, 3, 3, 0]} />
      </BarChart>
    </ResponsiveContainer>
  )
}

function CompareTable({ series, services, agg }: {
  series: ExploreValueSeries
  services: ServiceRef[]
  agg: ExploreAggregation
}) {
  const styles = useExploreStyles()
  const { t } = useTranslation()
  const rows = compareRows(series, services, agg)
  return (
    <div className={styles.tableScroll}>
      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>{t('analytics.common.service')}</TableHeaderCell>
            <TableHeaderCell>{aggregationLabel(agg, t)}</TableHeaderCell>
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
