import {
  Card, CardHeader, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Text,
} from '@fluentui/react-components'
import { useTranslation } from 'react-i18next'
import type { ExploreAggregation, ExploreValueSeries } from '../../../api/types'
import { formatPeriodLabel } from '../../../utils/periodFormat'
import { aggregationLabel, fmt, useExploreStyles, type ServiceRef } from '../shared'

/** The Snapshot sub-view: the latest period's value for every service and every numeric value. */
export function SnapshotView({ values, services, agg }: {
  values: ExploreValueSeries[]
  services: ServiceRef[]
  agg: ExploreAggregation
}) {
  const styles = useExploreStyles()
  const { t } = useTranslation()
  if (values.length === 0 || services.length === 0) {
    return <Card className={styles.card}><div className={styles.empty}>{t('analytics.explore.content.noSamples')}</div></Card>
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
        header={<Text weight="semibold">{t('analytics.explore.snapshot.title')}</Text>}
        description={
          <span className={styles.cardSub}>
            {t('analytics.explore.snapshot.description', { aggregation: aggregationLabel(agg, t) })}
          </span>
        }
      />
      <div className={styles.tableScroll}>
        <Table size="small">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>{t('analytics.common.service')}</TableHeaderCell>
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
