import { makeStyles, tokens } from '@fluentui/react-components'
import type {
  ExploreAggregation, ExploreServicePoint, ExploreServiceRef, ExploreValueSeries, RagStatus,
} from '../../api/types'
import { formatPeriodLabel } from '../../utils/periodFormat'

/** The outer tab: the cross-schema scorecard, the detailed per-schema analysis, or the anomaly board. */
export type OuterTab = 'scorecard' | 'analysis' | 'anomalies'

/** The inner analysis view, nested under the "Analysis" outer tab. */
export type ExploreView = 'trend' | 'compare' | 'snapshot'

/** A service appearing in an Explore result. Re-exported alias for the verbose wire type. */
export type ServiceRef = ExploreServiceRef

// Traffic-light colours for the RAG scorecard. Explicit hex so the indicators read as real
// red/amber/green in both light and dark themes rather than drifting with the Fluent palette.
export const RAG_COLORS: Record<RagStatus, string> = {
  Green: '#16a34a',
  Amber: '#d97706',
  Red: '#dc2626',
}
export const RAG_LABELS: Record<RagStatus, string> = { Green: 'On target', Amber: 'Warning', Red: 'Off target' }

// Neutral grey for "missing" cells (a service that didn't submit the requested period). Kept as an
// explicit hex like the RAG colours so it reads as a true grey in both themes.
export const MISSING_COLOR = '#9ca3af'
export const MISSING_LABEL = 'No submission'

// Anomaly board colours: green = within recent range, yellow/amber = statistical outlier. Missing
// reuses MISSING_COLOR / "No submission". Explicit hex to read true in both themes (like RAG).
export const ANOMALY_COLORS = { Normal: '#16a34a', Anomaly: '#d97706' } as const
export const ANOMALY_LABELS = { Normal: 'No anomalies', Anomaly: 'Anomaly' } as const

// Defaults and choices for the anomaly detector controls (shared by the Trend toggle and the
// Anomalies tab). Mirror the server-side defaults in AnomalyDetector.
export const ANOMALY_WINDOW_DEFAULT = 12
export const ANOMALY_THRESHOLD_DEFAULT = 2.5
export const ANOMALY_WINDOWS = [8, 12, 26] as const
export const ANOMALY_THRESHOLDS = [2, 2.5, 3] as const

export const AGGREGATIONS: ExploreAggregation[] = ['Average', 'Sum', 'Min', 'Max', 'Count']
export const AGG_LABELS: Record<ExploreAggregation, string> = {
  Average: 'Average', Sum: 'Sum', Min: 'Minimum', Max: 'Maximum', Count: 'Sample count',
}

// A small categorical palette that reads acceptably in both light and dark themes. Cycled when
// there are more services than colours.
export const SERIES_COLORS = [
  '#2563eb', '#dc2626', '#059669', '#d97706', '#7c3aed',
  '#0891b2', '#db2777', '#65a30d', '#475569', '#c026d3',
]

/** Shared styles for the Explore page chrome and every view it hosts. */
export const useExploreStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  filters: { display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' },
  // Mirror the small, muted filter labels used by the other PeriodFilter pages (e.g. AuditPage)
  // so the bundled "Period" label lines up with the rest of the row.
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  fieldLabel: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3, display: 'flex', alignItems: 'center', gap: '4px' },
  infoIcon: { color: tokens.colorNeutralForeground3, cursor: 'help' },
  dropdown: { minWidth: '200px' },
  statRow: { display: 'flex', gap: '12px', flexWrap: 'wrap' },
  stat: { padding: '12px 16px', minWidth: '120px' },
  statLabel: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  statValue: { fontSize: '24px', fontWeight: 700 },
  card: { padding: '16px' },
  cardHeader: { marginBottom: '8px' },
  cardHeaderRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  scSwitches: { display: 'flex', alignItems: 'center', gap: '12px', flexWrap: 'wrap' },
  cardSub: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  empty: { color: tokens.colorNeutralForeground3, padding: '32px 0', textAlign: 'center' },
  chartWrap: { width: '100%' },
  tableScroll: { overflowX: 'auto' },
  numCell: { textAlign: 'right', fontVariantNumeric: 'tabular-nums' },
  scSchema: { display: 'flex', flexDirection: 'column', gap: '12px' },
  scSchemaTitle: { display: 'flex', alignItems: 'baseline', gap: '8px' },
  // Clickable header used to collapse/expand a schema's card on the anomaly board.
  scCollapseHeader: {
    display: 'flex', alignItems: 'center', gap: '8px', width: '100%',
    backgroundColor: 'transparent', border: 'none', padding: 0, margin: 0,
    cursor: 'pointer', textAlign: 'left', color: 'inherit',
    ':hover': { color: tokens.colorNeutralForeground2 },
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '2px', borderRadius: '4px' },
  },
  scCollapseSummary: { color: tokens.colorNeutralForeground3, fontSize: '12px', fontWeight: 400 },
  scValueGroup: { display: 'flex', flexDirection: 'column', gap: '6px', marginTop: '4px' },
  scValueLabel: { fontSize: '13px', fontWeight: 600 },
  scGrid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: '8px' },
  scCard: {
    display: 'flex', alignItems: 'center', gap: '10px', padding: '10px 12px',
    backgroundColor: tokens.colorNeutralBackground1, borderRadius: '6px',
    border: `1px solid ${tokens.colorNeutralStroke2}`, borderLeftWidth: '4px',
    color: 'inherit', textDecoration: 'none', cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '1px' },
  },
  scDot: { width: '12px', height: '12px', borderRadius: '50%', flexShrink: 0 },
  scCardBody: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  scService: { fontWeight: 600, fontSize: '13px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  scMeta: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  scValueNum: { fontVariantNumeric: 'tabular-nums', fontWeight: 600 },
  scLegend: { display: 'flex', gap: '16px', alignItems: 'center', flexWrap: 'wrap', marginBottom: '4px' },
  scLegendItem: { display: 'flex', alignItems: 'center', gap: '6px', fontSize: '12px', color: tokens.colorNeutralForeground2 },
  // Custom chart tooltip (anomaly mode): a small themed card listing each line's value and z-score.
  tooltip: {
    backgroundColor: tokens.colorNeutralBackground1, border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px', padding: '8px 10px', boxShadow: tokens.shadow8, fontSize: '12px',
    display: 'flex', flexDirection: 'column', gap: '2px',
  },
  tooltipTitle: { fontWeight: 600, marginBottom: '2px' },
  tooltipRow: { display: 'flex', alignItems: 'center', gap: '6px' },
  // Anomaly detector controls grouped in a popover (shared by the Trend toggle and Anomalies tab).
  popover: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '12px', minWidth: '240px' },
})

export type ExploreStyles = ReturnType<typeof useExploreStyles>

/** Reduce a set of per-bucket points to one figure for the whole range, matching the aggregation. */
export function rollup(agg: ExploreAggregation, points: { value: number; count: number }[]): number {
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

export type CompareRow = { name: string; value: number }

/** Roll every service up to a single figure over the whole selection, ranked high-to-low. */
export function compareRows(
  series: ExploreValueSeries,
  services: ServiceRef[],
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

/** Build the header + rows for a CSV export of the currently active (non-scorecard) view. */
export function buildExportRows(
  view: ExploreView,
  values: ExploreValueSeries[],
  activeSeries: ExploreValueSeries | undefined,
  services: ServiceRef[],
  agg: ExploreAggregation,
  anomaly: boolean = false,
  combined: boolean = false,
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
  // trend — when anomaly highlighting is on in combined mode, the single overall line lets us add
  // unambiguous z / anomaly columns. (Per-service anomalies stay on the chart markers/tooltips.)
  if (anomaly && combined) {
    const headers = ['Period', 'All services', 'z', 'Anomaly']
    const rows = activeSeries.buckets.map(b => [
      formatPeriodLabel(b.periodStart, activeSeries.cadence),
      round(b.value),
      b.z === null || b.z === undefined ? '' : round(b.z),
      b.isAnomaly ? 'yes' : '',
    ])
    return { headers, rows, name: `trend-${activeSeries.valueName}` }
  }
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

export function valueTitle(series: ExploreValueSeries | undefined, fallback: string): string {
  if (!series) return fallback
  return (series.label || series.valueName) + (series.unit ? ` (${series.unit})` : '')
}

export function label(x: { label?: string | null; name: string }): string {
  return x.label || x.name
}

export function round(n: number): number {
  return Math.round(n * 1000) / 1000
}

export function fmt(n: number): string {
  if (!Number.isFinite(n)) return '—'
  return Number.isInteger(n) ? String(n) : String(Math.round(n * 100) / 100)
}

export function cell(v: number | string | undefined): string {
  if (v === undefined || v === '') return '—'
  return typeof v === 'number' ? fmt(v) : v
}
