import { type RefObject } from 'react'
import {
  Card, CardHeader, MessageBar, MessageBarBody, Switch, Text,
} from '@fluentui/react-components'
import { useTranslation } from 'react-i18next'
import type { ExploreAggregation, ExploreValueSeries, IngestEvent, SchemaValue } from '../../../api/types'
import {
  aggregationLabel, eventsForServices, fmt, rollup, useExploreStyles, valueTitle, type ExploreView, type ServiceRef,
} from '../shared'
import { AnomalySettings } from '../AnomalySettings'
import { TrendView } from './TrendView'
import { CompareView } from './CompareView'
import { SnapshotView } from './SnapshotView'

/** The anomaly-detection controls threaded down to the Trend view's header. */
export interface AnomalyControls {
  on: boolean
  window: number
  threshold: number
  robust: boolean
  onToggle: (v: boolean) => void
  onWindow: (n: number) => void
  onThreshold: (n: number) => void
  onRobust: (b: boolean) => void
}

/**
 * The "traditional" Explore content: the Trend, Compare and Snapshot views plus their shared chrome
 * (stat cards, card header, per-view toggles). Presentational — all URL/query state lives in the
 * parent `ExplorePage`, which feeds this component data and toggle callbacks.
 */
export function ExploreContent({
  view, schemaName, noNumeric, isLoading,
  values, services, agg,
  activeSeries, prevActiveSeries, activeValue, activeValueName,
  combined, asTable, projecting, comparing, previousLabel, chartRef, anomaly,
  events, canShowEvents, eventsOn, onToggleEvents,
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
  anomaly: AnomalyControls
  /** Live events (unfiltered by service scope) fetched for the current period window; empty when the caller can't read events. */
  events: IngestEvent[]
  /** Whether the caller holds `events:read` — gates the "Show events" toggle and any overlay rendering. */
  canShowEvents: boolean
  eventsOn: boolean
  onToggleEvents: (v: boolean) => void
  onToggleCombined: (v: boolean) => void
  onToggleProjection: (v: boolean) => void
  onToggleTable: (v: boolean) => void
}) {
  const styles = useExploreStyles()
  const { t } = useTranslation()

  if (!schemaName) {
    return <Card className={styles.card}><div className={styles.empty}>{t('analytics.explore.content.pickSchema')}</div></Card>
  }
  if (noNumeric) {
    return (
      <MessageBar intent="info">
        <MessageBarBody>
          {t('analytics.explore.content.noNumeric')}
        </MessageBarBody>
      </MessageBar>
    )
  }
  if (isLoading) {
    return <Card className={styles.card}><div className={styles.empty}>{t('analytics.common.loading')}</div></Card>
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
            header={<Text weight="semibold">{t(view === 'trend' ? 'analytics.explore.content.trendOverTime' : 'analytics.explore.views.compare')}</Text>}
            description={
              <span className={styles.cardSub}>
                {t('analytics.explore.content.aggregationOf', {
                  aggregation: aggregationLabel(agg, t),
                  value: valueTitle(activeSeries, activeValueName),
                })}
                {activeSeries?.cadence
                  ? ` · ${t('analytics.explore.content.perCadencePeriod', {
                    cadence: t(`analytics.cadence.${activeSeries.cadence.toLowerCase()}`).toLowerCase(),
                  })}`
                  : ''}
              </span>
            }
          />
          <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
            {view === 'trend' && (
              <Switch label={t('analytics.explore.content.combineServices')} checked={combined} onChange={(_, d) => onToggleCombined(!!d.checked)} />
            )}
            {view === 'trend' && !asTable && (
              <Switch label={t('analytics.explore.content.projection')} checked={projecting} onChange={(_, d) => onToggleProjection(!!d.checked)} />
            )}
            <Switch label={t('analytics.explore.content.viewAsTable')} checked={asTable} onChange={(_, d) => onToggleTable(!!d.checked)} />
            {view === 'trend' && !asTable && canShowEvents && (
              <Switch label={t('analytics.explore.content.showEvents')} checked={eventsOn} onChange={(_, d) => onToggleEvents(!!d.checked)} />
            )}
            {view === 'trend' && (
              <AnomalySettings
                enabled={anomaly.on}
                onToggleEnabled={anomaly.onToggle}
                window={anomaly.window}
                threshold={anomaly.threshold}
                robust={anomaly.robust}
                onWindow={anomaly.onWindow}
                onThreshold={anomaly.onThreshold}
                onRobust={anomaly.onRobust}
              />
            )}
          </div>
        </div>

        {!activeSeries || activeSeries.buckets.length === 0 ? (
          <div className={styles.empty}>{t('analytics.explore.content.noSamples')}</div>
        ) : view === 'trend' ? (
          <TrendView
            series={activeSeries}
            services={services}
            combined={combined}
            projecting={projecting}
            previous={prevActiveSeries}
            previousLabel={comparing ? previousLabel : undefined}
            band={activeValue}
            anomaly={anomaly.on}
            asTable={asTable}
            chartRef={chartRef}
            events={canShowEvents ? eventsForServices(events, services) : []}
            showEvents={canShowEvents && eventsOn}
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
  const { t } = useTranslation()
  if (!series || series.buckets.length === 0) return null
  const totalSamples = series.buckets.reduce((acc, b) => acc + b.count, 0)
  const latest = series.buckets[series.buckets.length - 1]
  const overall = rollup(agg, series.buckets.map(b => ({ value: b.value, count: b.count })))
  const unit = series.unit ? ` ${series.unit}` : ''
  return (
    <div className={styles.statRow}>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>{t('analytics.explore.content.overallAggregation', { aggregation: aggregationLabel(agg, t) })}</span>
        <span className={styles.statValue}>{fmt(overall)}{unit}</span>
      </Card>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>{t('analytics.explore.content.latestPeriod')}</span>
        <span className={styles.statValue}>{fmt(latest.value)}{unit}</span>
      </Card>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>{t('analytics.explore.content.samples')}</span>
        <span className={styles.statValue}>{totalSamples}</span>
      </Card>
      <Card className={styles.stat}>
        <span className={styles.statLabel}>{t('analytics.explore.content.periodsServices')}</span>
        <span className={styles.statValue}>{series.buckets.length} / {serviceCount}</span>
      </Card>
    </div>
  )
}
