import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Card, Switch, Text } from '@fluentui/react-components'
import { ChevronDown20Regular, ChevronRight20Regular } from '@fluentui/react-icons'
import type { ExploreScorecard, ExploreScorecardCell, ExploreScorecardSchema, RagStatus } from '../../../api/types'
import {
  fmt, MISSING_COLOR, MISSING_LABEL, RAG_COLORS, RAG_LABELS, useExploreStyles, type ExploreStyles,
} from '../shared'

const cellColor = (c: ExploreScorecardCell) => (c.status ? RAG_COLORS[c.status] : MISSING_COLOR)
const cellLabel = (c: ExploreScorecardCell) => (c.status ? RAG_LABELS[c.status] : MISSING_LABEL)

/** A short "3 on target · 1 warning · 2 no submission" summary for a collapsed schema card. */
function summarize(schema: ExploreScorecardSchema): string {
  let green = 0, amber = 0, red = 0, missing = 0
  for (const v of schema.values) {
    for (const c of v.cells) {
      if (c.status === 'Green') green++
      else if (c.status === 'Amber') amber++
      else if (c.status === 'Red') red++
      else missing++
    }
  }
  const parts: string[] = []
  if (green) parts.push(`${green} ${RAG_LABELS.Green.toLowerCase()}`)
  if (amber) parts.push(`${amber} ${RAG_LABELS.Amber.toLowerCase()}`)
  if (red) parts.push(`${red} ${RAG_LABELS.Red.toLowerCase()}`)
  if (missing) parts.push(`${missing} no submission`)
  return parts.join(' · ')
}

/** Drop cells failing the keep test, then prune values and schemas left with nothing to show. */
function filterCells(
  schemas: ExploreScorecardSchema[],
  keep: (c: ExploreScorecardCell) => boolean,
): ExploreScorecardSchema[] {
  return schemas
    .map(schema => ({
      ...schema,
      values: schema.values
        .map(v => ({ ...v, cells: v.cells.filter(keep) }))
        .filter(v => v.cells.length > 0),
    }))
    .filter(schema => schema.values.length > 0)
}

/**
 * Cross-schema Red/Amber/Green status board. Renders one card per reporting service for every
 * banded numeric value, grouped under each schema. Status colours and the empty/loading states are
 * self-contained; all data shaping happens server-side (see `GET /api/admin/explore/scorecard`).
 */
export function ScorecardView({
  data, isLoading, onlyIssues, onToggleOnlyIssues, hideMissing, onToggleHideMissing,
}: {
  data: ExploreScorecard | undefined
  isLoading: boolean
  onlyIssues: boolean
  onToggleOnlyIssues: (v: boolean) => void
  hideMissing: boolean
  onToggleHideMissing: (v: boolean) => void
}) {
  const styles = useExploreStyles()

  if (isLoading) {
    return <Card className={styles.card}><div className={styles.empty}>Loading…</div></Card>
  }
  if (!data || data.schemas.length === 0) {
    return (
      <Card className={styles.card}>
        <div className={styles.empty}>
          No KPIs with a target band yet. Add an acceptable/ideal range to a numeric schema value to see it here.
        </div>
      </Card>
    )
  }

  const serviceLabel = new Map(data.services.map(svc => [svc.serviceId, svc.serviceLabel || svc.serviceName]))
  let schemas = data.schemas
  if (onlyIssues) schemas = filterCells(schemas, c => c.status !== 'Green')
  if (hideMissing) schemas = filterCells(schemas, c => c.status !== null)
  // Only advertise the "missing" swatch when the current view actually contains missing cells
  // (i.e. a last-period mode), so the latest-available board keeps a three-state legend.
  const hasMissing = data.schemas.some(s => s.values.some(v => v.cells.some(c => c.status === null)))

  return (
    <>
      <div className={styles.cardHeaderRow}>
        <div className={styles.scLegend}>
          {(['Green', 'Amber', 'Red'] as RagStatus[]).map(st => (
            <span key={st} className={styles.scLegendItem}>
              <span className={styles.scDot} style={{ backgroundColor: RAG_COLORS[st] }} />
              {RAG_LABELS[st]}
            </span>
          ))}
          {hasMissing && (
            <span className={styles.scLegendItem}>
              <span className={styles.scDot} style={{ backgroundColor: MISSING_COLOR }} />
              {MISSING_LABEL}
            </span>
          )}
        </div>
        <div className={styles.scSwitches}>
          <Switch label="Hide on-target" checked={onlyIssues} onChange={(_, d) => onToggleOnlyIssues(!!d.checked)} />
          {hasMissing && (
            <Switch label="Hide missing" checked={hideMissing} onChange={(_, d) => onToggleHideMissing(!!d.checked)} />
          )}
        </div>
      </div>
      {schemas.length === 0 ? (
        <Card className={styles.card}><div className={styles.empty}>Every KPI is on target.</div></Card>
      ) : schemas.map(schema => (
        <SchemaCard key={schema.schemaName} schema={schema} serviceLabel={serviceLabel} styles={styles} />
      ))}
    </>
  )
}

/** One schema's collapsible card on the scorecard. Always expanded by default; collapsed cards show a one-line state summary. */
function SchemaCard({ schema, serviceLabel, styles }: {
  schema: ExploreScorecardSchema
  serviceLabel: Map<string, string>
  styles: ExploreStyles
}) {
  const [expanded, setExpanded] = useState(true)

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
                  const periodDate = new Date(c.periodStart).toLocaleDateString(undefined, { timeZone: 'UTC' })
                  const body = (
                    <>
                      <span className={styles.scDot} style={{ backgroundColor: cellColor(c) }} />
                      <div className={styles.scCardBody}>
                        <span className={styles.scService}>{serviceLabel.get(c.serviceId) ?? c.serviceId}</span>
                        <span className={styles.scMeta}>
                          {c.value === null ? (
                            <>{MISSING_LABEL}{' · '}{periodDate}</>
                          ) : (
                            <>
                              <span className={styles.scValueNum}>{fmt(c.value)}</span>{v.unit ? ` ${v.unit}` : ''}
                              {' · as of '}{periodDate}
                            </>
                          )}
                        </span>
                      </div>
                    </>
                  )
                  return c.submissionId ? (
                    <Link
                      key={c.serviceId}
                      to={`/submissions/${c.submissionId}/view`}
                      className={styles.scCard}
                      style={{ borderLeftColor: cellColor(c) }}
                      title={`View submission · ${cellLabel(c)}`}
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
