import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Card, Switch, Text } from '@fluentui/react-components'
import { ChevronDown20Regular, ChevronRight20Regular } from '@fluentui/react-icons'
import type { ExploreAnomalies, ExploreAnomalyCell, ExploreAnomalySchema } from '../../../api/types'
import {
  ANOMALY_COLORS, ANOMALY_LABELS, fmt, MISSING_COLOR, MISSING_LABEL,
  useExploreStyles, type ExploreStyles,
} from '../shared'

const cellColor = (c: ExploreAnomalyCell) =>
  c.state === 'Anomaly' ? ANOMALY_COLORS.Anomaly : c.state === 'Normal' ? ANOMALY_COLORS.Normal : MISSING_COLOR
const cellLabel = (c: ExploreAnomalyCell) =>
  c.state === 'Anomaly' ? ANOMALY_LABELS.Anomaly : c.state === 'Normal' ? ANOMALY_LABELS.Normal : MISSING_LABEL

/** Drop cells failing the keep test, then prune values and schemas left with nothing to show. */
function filterCells(
  schemas: ExploreAnomalySchema[],
  keep: (c: ExploreAnomalyCell) => boolean,
): ExploreAnomalySchema[] {
  return schemas
    .map(schema => ({
      ...schema,
      values: schema.values
        .map(v => ({ ...v, cells: v.cells.filter(keep) }))
        .filter(v => v.cells.length > 0),
    }))
    .filter(schema => schema.values.length > 0)
}

/** Tally a schema's cells by state across all its values. */
function countStates(schema: ExploreAnomalySchema): { anomaly: number; normal: number; missing: number } {
  let anomaly = 0, normal = 0, missing = 0
  for (const v of schema.values) {
    for (const c of v.cells) {
      if (c.state === 'Anomaly') anomaly++
      else if (c.state === 'Normal') normal++
      else missing++
    }
  }
  return { anomaly, normal, missing }
}

/** A short "2 anomalies · 5 normal · 1 no submission" summary for a collapsed schema card. */
function summarize(schema: ExploreAnomalySchema): string {
  const { anomaly, normal, missing } = countStates(schema)
  const parts: string[] = []
  if (anomaly) parts.push(`${anomaly} ${anomaly === 1 ? 'anomaly' : 'anomalies'}`)
  if (normal) parts.push(`${normal} normal`)
  if (missing) parts.push(`${missing} no submission`)
  return parts.join(' · ')
}

/**
 * Per-period anomaly board: one card per applicable service for each numeric value, grouped by
 * schema. Green = within recent range, yellow = statistical outlier, grey = didn't submit. Clicking
 * a submitted card jumps to the Analysis Trend view for that schema/value/service with anomaly
 * highlighting already on, so the operator lands on the exact chart that flagged it.
 */
export function AnomaliesView({
  data, isLoading, hideNormal, onToggleHideNormal, hideMissing, onToggleHideMissing,
  period, window, threshold, robust,
}: {
  data: ExploreAnomalies | undefined
  isLoading: boolean
  hideNormal: boolean
  onToggleHideNormal: (v: boolean) => void
  hideMissing: boolean
  onToggleHideMissing: (v: boolean) => void
  period: 'current' | 'closed'
  window: number
  threshold: number
  robust: boolean
}) {
  const styles = useExploreStyles()

  if (isLoading) {
    return <Card className={styles.card}><div className={styles.empty}>Loading…</div></Card>
  }
  if (!data || data.schemas.length === 0) {
    return (
      <Card className={styles.card}>
        <div className={styles.empty}>
          No numeric KPIs to scan yet. Pick one or more schemas with numeric values to check the latest period for anomalies.
        </div>
      </Card>
    )
  }

  const serviceLabel = new Map(data.services.map(svc => [svc.serviceId, svc.serviceLabel || svc.serviceName]))
  let schemas = data.schemas
  if (hideNormal) schemas = filterCells(schemas, c => c.state !== 'Normal')
  if (hideMissing) schemas = filterCells(schemas, c => c.value !== null)
  const hasMissing = data.schemas.some(s => s.values.some(v => v.cells.some(c => c.value === null)))

  // Deep link into the Analysis Trend view with the detector settings carried across so the chart
  // reproduces exactly what flagged the cell.
  const analysisLink = (schemaName: string, valueName: string, serviceId: string): string => {
    const sp = new URLSearchParams()
    sp.set('tab', 'analysis')
    sp.set('view', 'trend')
    sp.set('schema', schemaName)
    sp.set('value', valueName)
    sp.set('services', serviceId)
    sp.set('az', '1')
    sp.set('awin', String(window))
    sp.set('athr', String(threshold))
    if (robust) sp.set('arob', '1')
    return `/explore?${sp.toString()}`
  }

  return (
    <>
      <div className={styles.cardHeaderRow}>
        <div className={styles.scLegend}>
          <span className={styles.scLegendItem}>
            <span className={styles.scDot} style={{ backgroundColor: ANOMALY_COLORS.Normal }} />
            {ANOMALY_LABELS.Normal}
          </span>
          <span className={styles.scLegendItem}>
            <span className={styles.scDot} style={{ backgroundColor: ANOMALY_COLORS.Anomaly }} />
            {ANOMALY_LABELS.Anomaly}
          </span>
          <span className={styles.scLegendItem}>
            <span className={styles.scDot} style={{ backgroundColor: MISSING_COLOR }} />
            {MISSING_LABEL}
          </span>
        </div>
        <div className={styles.scSwitches}>
          <Switch label="Hide normal" checked={hideNormal} onChange={(_, d) => onToggleHideNormal(!!d.checked)} />
          {hasMissing && (
            <Switch label="Hide missing" checked={hideMissing} onChange={(_, d) => onToggleHideMissing(!!d.checked)} />
          )}
        </div>
      </div>
      {schemas.length === 0 ? (
        <Card className={styles.card}>
          <div className={styles.empty}>No anomalies in the {period === 'closed' ? 'latest closed' : 'current'} period.</div>
        </Card>
      ) : schemas.map(schema => (
        <SchemaCard
          key={schema.schemaName}
          schema={schema}
          serviceLabel={serviceLabel}
          analysisLink={analysisLink}
          styles={styles}
        />
      ))}
    </>
  )
}

/**
 * One schema's collapsible card on the anomaly board. Expanded by default, but collapsed when every
 * cell is a grey "no submission" (nothing to act on yet) — the operator can still expand it to see
 * who's outstanding. Collapsed cards show a one-line state summary.
 */
function SchemaCard({ schema, serviceLabel, analysisLink, styles }: {
  schema: ExploreAnomalySchema
  serviceLabel: Map<string, string>
  analysisLink: (schemaName: string, valueName: string, serviceId: string) => string
  styles: ExploreStyles
}) {
  const counts = countStates(schema)
  const allMissing = counts.anomaly === 0 && counts.normal === 0
  const [expanded, setExpanded] = useState(!allMissing)

  return (
    <Card className={styles.card}>
      <div className={styles.scSchema}>
        <button
          type="button"
          className={styles.scCollapseHeader}
          aria-expanded={expanded}
          onClick={() => setExpanded(e => !e)}
        >
          {expanded ? <ChevronDown20Regular /> : <ChevronRight20Regular />}
          <Text weight="semibold" size={400}>{schema.schemaLabel || schema.schemaName}</Text>
          {!expanded && <span className={styles.scCollapseSummary}>{summarize(schema)}</span>}
        </button>
        {expanded && schema.values.map(v => (
          <div key={v.valueName} className={styles.scValueGroup}>
            <span className={styles.scValueLabel}>{v.label || v.valueName}{v.unit ? ` (${v.unit})` : ''}</span>
            <div className={styles.scGrid}>
              {[...v.cells]
                .sort((a, b) => (serviceLabel.get(a.serviceId) ?? '').localeCompare(serviceLabel.get(b.serviceId) ?? ''))
                .map(c => {
                  const body = (
                    <>
                      <span className={styles.scDot} style={{ backgroundColor: cellColor(c) }} />
                      <div className={styles.scCardBody}>
                        <span className={styles.scService}>{serviceLabel.get(c.serviceId) ?? c.serviceId}</span>
                        <span className={styles.scMeta}>
                          {c.value === null ? (
                            MISSING_LABEL
                          ) : (
                            <>
                              <span className={styles.scValueNum}>{fmt(c.value)}</span>{v.unit ? ` ${v.unit}` : ''}
                              {c.z !== null ? ` · z=${fmt(c.z)}` : ''}
                            </>
                          )}
                        </span>
                      </div>
                    </>
                  )
                  return c.value !== null ? (
                    <Link
                      key={c.serviceId}
                      to={analysisLink(schema.schemaName, v.valueName, c.serviceId)}
                      className={styles.scCard}
                      style={{ borderLeftColor: cellColor(c) }}
                      title={`Open in Analysis · ${cellLabel(c)}`}
                    >
                      {body}
                    </Link>
                  ) : (
                    <div
                      key={c.serviceId}
                      className={styles.scCard}
                      style={{ borderLeftColor: cellColor(c), cursor: 'default' }}
                      title={cellLabel(c)}
                    >
                      {body}
                    </div>
                  )
                })}
            </div>
          </div>
        ))}
      </div>
    </Card>
  )
}
