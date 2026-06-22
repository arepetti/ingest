import { type RefObject } from 'react'
import {
  Card, CardHeader, MessageBar, MessageBarBody, Switch, Text,
} from '@fluentui/react-components'
import type { ExploreAggregation, ExploreValueSeries, SchemaValue } from '../../../api/types'
import { cadenceLabel } from '../../../utils/cadence'
import {
  AGG_LABELS, fmt, rollup, useExploreStyles, valueTitle, type ExploreView, type ServiceRef,
} from '../shared'
import { TrendView } from './TrendView'
import { CompareView } from './CompareView'
import { SnapshotView } from './SnapshotView'

/**
 * The "traditional" Explore content: the Trend, Compare and Snapshot views plus their shared chrome
 * (stat cards, card header, per-view toggles). Presentational — all URL/query state lives in the
 * parent `ExplorePage`, which feeds this component data and toggle callbacks.
 */
export function ExploreContent({
  view, schemaName, noNumeric, isLoading,
  values, services, agg,
  activeSeries, prevActiveSeries, activeValue, activeValueName,
  combined, asTable, projecting, comparing, previousLabel, chartRef,
  onToggleCombined, onToggleProjection, onToggleTable,
}: {
  view: ExploreView
  schemaName: string
  noNumeric: boolean
  isLoading: boolean
  values: ExploreValueSeries[]
  services: ServiceRef[]
  agg: ExploreAggregation
  activeSeries?: ExploreValueSeries
  prevActiveSeries?: ExploreValueSeries
  activeValue?: SchemaValue
  activeValueName: string
  combined: boolean
  asTable: boolean
  projecting: boolean
  comparing: boolean
  previousLabel?: string
  chartRef: RefObject<HTMLDivElement | null>
  onToggleCombined: (v: boolean) => void
  onToggleProjection: (v: boolean) => void
  onToggleTable: (v: boolean) => void
}) {
  const styles = useExploreStyles()

  if (!schemaName) {
    return <Card className={styles.card}><div className={styles.empty}>Pick a schema to start exploring.</div></Card>
  }
  if (noNumeric) {
    return (
      <MessageBar intent="info">
        <MessageBarBody>
          This schema has no numeric values, so there's nothing to chart. Add a Number or Integer value to explore it.
        </MessageBarBody>
      </MessageBar>
    )
  }
  if (isLoading) {
    return <Card className={styles.card}><div className={styles.empty}>Loading…</div></Card>
  }
  if (view === 'snapshot') {
    return <SnapshotView values={values} services={services} agg={agg} />
  }

  return (
    <>
      <StatRow series={activeSeries} agg={agg} serviceCount={services.length} />
      <Card className={styles.card}>
        <div className={styles.cardHeaderRow}>
          <CardHeader
            className={styles.cardHeader}
            header={<Text weight="semibold">{view === 'trend' ? 'Trend over time' : 'Compare services'}</Text>}
            description={
              <span className={styles.cardSub}>
                {AGG_LABELS[agg]} of {valueTitle(activeSeries, activeValueName)}
                {activeSeries?.cadence ? ` · per ${cadenceLabel(activeSeries.cadence).toLowerCase()} period` : ''}
              </span>
            }
          />
          <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
            {view === 'trend' && (
              <Switch label="Combine services" checked={combined} onChange={(_, d) => onToggleCombined(!!d.checked)} />
            )}
            {view === 'trend' && !asTable && (
              <Switch label="Projection" checked={projecting} onChange={(_, d) => onToggleProjection(!!d.checked)} />
            )}
            <Switch label="View as table" checked={asTable} onChange={(_, d) => onToggleTable(!!d.checked)} />
          </div>
        </div>

        {!activeSeries || activeSeries.buckets.length === 0 ? (
          <div className={styles.empty}>No samples for this selection.</div>
        ) : view === 'trend' ? (
          <TrendView
            series={activeSeries}
            services={services}
            combined={combined}
            projecting={projecting}
            previous={prevActiveSeries}
            previousLabel={comparing ? previousLabel : undefined}
            band={activeValue}
            asTable={asTable}
            chartRef={chartRef}
          />
        ) : (
          <CompareView
            series={activeSeries}
            services={services}
            agg={agg}
            asTable={asTable}
            chartRef={chartRef}
          />
        )}
      </Card>
    </>
  )
}

function StatRow({ series, agg, serviceCount }: {
  series: ExploreValueSeries | undefined
  agg: ExploreAggregation
  serviceCount: number
}) {
  const styles = useExploreStyles()
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
