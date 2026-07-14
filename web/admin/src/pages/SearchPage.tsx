import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  Badge, Button, SearchBox, Spinner, Text, Title2, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  CalendarLtr20Regular, DataTreemap20Regular,
  DocumentBulletList20Regular, DocumentText20Regular, PeopleTeam20Regular,
  Rocket20Regular, Search20Regular, Tag20Regular,
} from '@fluentui/react-icons'
import { useCapabilities, useAccounts, useEvents, useReports, useSchemas, useSubmissions } from '../api/hooks'
import type { Schema, SchemaValue, Submission } from '../api/types'
import { formatDateTime } from '../utils/format'
import { eventKindLabel } from '../utils/eventKind'
import { actionIcon, matchActions, scoreFields, type SearchAction } from './search/actions'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '20px', maxWidth: '860px' },
  header: { display: 'flex', flexDirection: 'column', gap: '12px' },
  searchRow: { display: 'flex', alignItems: 'center', gap: '8px', width: '100%', maxWidth: '620px' },
  searchBox: { flex: 1 },
  hint: { color: tokens.colorNeutralForeground3 },
  section: { display: 'flex', flexDirection: 'column', gap: '8px' },
  sectionHead: {
    display: 'flex', alignItems: 'center', gap: '8px',
    color: tokens.colorNeutralForeground2,
  },
  rows: { display: 'flex', flexDirection: 'column', gap: '2px' },
  row: {
    display: 'flex', alignItems: 'center', gap: '12px',
    padding: '10px 12px', borderRadius: tokens.borderRadiusMedium,
    textDecoration: 'none', color: tokens.colorNeutralForeground1,
    border: `1px solid transparent`,
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '1px' },
  },
  rowIcon: {
    flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
    width: '32px', height: '32px', borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground2,
  },
  rowText: { display: 'flex', flexDirection: 'column', minWidth: 0, gap: '2px' },
  rowPrimary: { fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  rowSecondary: {
    fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3,
    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
  },
  viewAll: {
    padding: '6px 12px', fontSize: tokens.fontSizeBase200,
    color: tokens.colorBrandForeground1, textDecoration: 'none',
    ':hover': { textDecoration: 'underline' },
  },
  empty: { color: tokens.colorNeutralForeground3, padding: '24px 0' },
})

/** How many entity results to show per category before offering a "view all" link. */
const PER_CATEGORY = 5

interface ResultItem {
  key: string
  to: string
  icon?: ReactNode
  primary: string
  secondary?: string
}

/**
 * Global search: a static, capability-gated action catalogue (resolved instantly) plus per-entity
 * result blocks. Each entity category is backed by its own query, so blocks appear independently as
 * their data arrives rather than waiting on the slowest one.
 */
export function SearchPage() {
  const s = useStyles()
  const [sp, setSp] = useSearchParams()
  const q = sp.get('q') ?? ''
  const query = q.trim()
  const enabled = query.length > 0
  const { has } = useCapabilities()

  // The textbox holds a draft; results read the committed `q` from the URL. Searching (Enter or the
  // Search button) commits the draft. Keep the draft in step when `q` changes from outside (e.g. the
  // top-bar search navigating here).
  const [draft, setDraft] = useState(q)
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- mirror the committed query into the box
    setDraft(q)
  }, [q])

  const commit = () => {
    const value = draft.trim()
    setSp(prev => {
      const next = new URLSearchParams(prev)
      if (value) next.set('q', value)
      else next.delete('q')
      return next
    }, { replace: true })
  }

  // Each list is fetched only once a query exists and only when the caller can read it. They're
  // independent React Query hooks, so each result block streams in on its own timeline.
  const schemasQ = useSchemas(undefined, enabled && has('schemas:read'))
  const accountsQ = useAccounts({ pageSize: 500 }, enabled && has('accounts:read'))
  const eventsQ = useEvents({ pageSize: 500 }, enabled && has('events:read'))
  const reportsQ = useReports(undefined, enabled && has('reports:read'))
  const submissionsQ = useSubmissions({ page: 1, pageSize: 200 }, enabled && has('submissions:read'))

  const actions = useMemo(
    () => matchActions(query, (a: SearchAction) => !a.capabilities || a.capabilities.some(c => has(c))),
    // `has` closes over the capability set; re-run when the query changes (the set is stable per render).
    [query, has],
  )

  const schemas = useMemo(() => rank(schemasQ.data?.items, q, sc => [sc.label, sc.name, sc.description]), [schemasQ.data, q])
  const values = useMemo(() => rankValues(schemasQ.data?.items, q), [schemasQ.data, q])
  const accounts = useMemo(() => rank(accountsQ.data?.items, q, a => [a.label, a.name, a.description, a.email]), [accountsQ.data, q])
  const events = useMemo(() => rank(eventsQ.data?.items, q, e => [e.label, e.description]), [eventsQ.data, q])
  const reports = useMemo(() => rank(reportsQ.data?.items, q, r => [r.label, r.name, r.description]), [reportsQ.data, q])
  const submissions = useMemo(
    () => rank(submissionsQ.data?.items, q, sub => [sub.serviceName, sub.id, ...schemaNames(sub)]),
    [submissionsQ.data, q],
  )

  const anyLoading =
    schemasQ.isLoading || accountsQ.isLoading || eventsQ.isLoading || reportsQ.isLoading || submissionsQ.isLoading
  const totalMatches =
    actions.length + schemas.length + values.length + accounts.length + events.length + reports.length + submissions.length

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Title2>Search</Title2>
        <div className={s.searchRow}>
          <SearchBox
            className={s.searchBox}
            size="large"
            placeholder="Search actions, schemas, values, events, accounts…"
            value={draft}
            onChange={(_, d) => setDraft(d.value)}
            onKeyDown={e => { if (e.key === 'Enter') commit() }}
          />
          <Button appearance="primary" size="large" icon={<Search20Regular />} onClick={commit}>
            Search
          </Button>
        </div>
      </div>

      {!enabled && (
        <Text className={s.hint}>
          Type what you want to do (for example “add user”, “submit”, “scorecard”) or the name of a
          schema, value, event, account or report.
        </Text>
      )}

      {enabled && (
        <>
          <ResultSection
            title="Actions"
            icon={<Rocket20Regular />}
            items={actions.map(a => ({ key: a.id, to: a.to, icon: actionIcon(a), primary: a.title, secondary: a.description }))}
          />

          <ResultSection
            title="Schemas"
            icon={<DataTreemap20Regular />}
            loading={schemasQ.isLoading}
            items={schemas.slice(0, PER_CATEGORY).map(sc => ({
              key: sc.id,
              to: `/schemas/${encodeURIComponent(sc.name)}/edit`,
              icon: <DataTreemap20Regular />,
              primary: sc.label || sc.name,
              secondary: sc.label ? sc.name : `${sc.values.length} value${sc.values.length === 1 ? '' : 's'}`,
            }))}
            viewAllTo={schemas.length > PER_CATEGORY ? '/schemas' : undefined}
            viewAllLabel={`View all ${schemas.length} schemas`}
          />

          <ResultSection
            title="Schema values"
            icon={<Tag20Regular />}
            loading={schemasQ.isLoading}
            items={values.slice(0, PER_CATEGORY).map(({ schema, value }) => ({
              key: `${schema.id}:${value.name}`,
              to: `/schemas/${encodeURIComponent(schema.name)}/edit`,
              icon: <Tag20Regular />,
              primary: value.label || value.name,
              secondary: `in ${schema.label || schema.name}`,
            }))}
            viewAllTo={values.length > PER_CATEGORY ? `/schemas/${encodeURIComponent(values[0].schema.name)}/edit` : undefined}
            viewAllLabel={`${values.length} matching values`}
          />

          <ResultSection
            title="Accounts"
            icon={<PeopleTeam20Regular />}
            loading={accountsQ.isLoading}
            items={accounts.slice(0, PER_CATEGORY).map(a => ({
              key: a.id,
              to: a.role === 'Service' ? `/services/${encodeURIComponent(a.name)}/status` : '/services',
              icon: <PeopleTeam20Regular />,
              primary: a.label || a.name,
              secondary: `${a.kind} · ${a.role}`,
            }))}
            viewAllTo={accounts.length > PER_CATEGORY ? '/services' : undefined}
            viewAllLabel={`View all ${accounts.length} accounts`}
          />

          <ResultSection
            title="Events"
            icon={<CalendarLtr20Regular />}
            loading={eventsQ.isLoading}
            items={events.slice(0, PER_CATEGORY).map(e => ({
              key: e.id,
              to: '/events',
              icon: <CalendarLtr20Regular />,
              primary: e.label,
              secondary: `${eventKindLabel(e.kind)} · ${formatDateTime(e.timestamp)}`,
            }))}
            viewAllTo={events.length > PER_CATEGORY ? '/events' : undefined}
            viewAllLabel={`View all ${events.length} events`}
          />

          <ResultSection
            title="Reports"
            icon={<DocumentText20Regular />}
            loading={reportsQ.isLoading}
            items={reports.slice(0, PER_CATEGORY).map(r => ({
              key: r.id,
              to: `/reports/${encodeURIComponent(r.name)}`,
              icon: <DocumentText20Regular />,
              primary: r.label || r.name,
              secondary: r.type === 'Single' ? 'Single submission report' : 'Aggregate report',
            }))}
            viewAllTo={reports.length > PER_CATEGORY ? '/reports' : undefined}
            viewAllLabel={`View all ${reports.length} reports`}
          />

          <ResultSection
            title="Submissions"
            icon={<DocumentBulletList20Regular />}
            loading={submissionsQ.isLoading}
            items={submissions.slice(0, PER_CATEGORY).map(sub => ({
              key: sub.id,
              to: `/submissions/${encodeURIComponent(sub.id)}`,
              icon: <DocumentBulletList20Regular />,
              primary: sub.serviceName || sub.id,
              secondary: [schemaNames(sub).join(', ') || 'No schemas', formatDateTime(sub.submittedAt)].join(' · '),
            }))}
            viewAllTo={submissions.length > PER_CATEGORY ? '/submissions' : undefined}
            viewAllLabel={`${submissions.length} recent matches`}
          />

          {!anyLoading && totalMatches === 0 && (
            <div className={s.empty}>No results for “{query}”. Try a different term.</div>
          )}
        </>
      )}
    </div>
  )
}

/** One titled block of results. Hidden entirely once loaded with nothing to show, so empty
 * categories don't clutter the page; while its query is loading it shows a small spinner. */
function ResultSection({
  title, icon, loading = false, items, viewAllTo, viewAllLabel,
}: {
  title: string
  icon: ReactNode
  loading?: boolean
  items: ResultItem[]
  viewAllTo?: string
  viewAllLabel?: string
}) {
  const s = useStyles()
  if (!loading && items.length === 0) return null
  return (
    <section className={s.section} aria-label={title}>
      <div className={s.sectionHead}>
        {icon}
        <Text weight="semibold">{title}</Text>
        {loading
          ? <Spinner size="tiny" label="Searching…" labelPosition="after" />
          : <Badge appearance="tint" color="informative">{items.length}</Badge>}
      </div>
      {!loading && (
        <div className={s.rows}>
          {items.map(it => (
            <Link key={it.key} to={it.to} className={s.row}>
              {it.icon && <span className={s.rowIcon}>{it.icon}</span>}
              <span className={s.rowText}>
                <span className={s.rowPrimary}>{it.primary}</span>
                {it.secondary && <span className={s.rowSecondary}>{it.secondary}</span>}
              </span>
            </Link>
          ))}
          {viewAllTo && <Link to={viewAllTo} className={s.viewAll}>{viewAllLabel ?? 'View all'}</Link>}
        </div>
      )}
    </section>
  )
}

/** Unique schema names referenced by a submission's samples. */
function schemaNames(sub: Submission): string[] {
  return [...new Set(sub.samples.map(sm => sm.schemaName).filter(Boolean))]
}

/** Score a list of items by a field selector, keeping only matches, best first. */
function rank<T>(items: T[] | undefined, query: string, fields: (item: T) => (string | null | undefined)[]): T[] {
  if (!query.trim() || !items) return []
  return items
    .map(item => ({ item, score: scoreFields(query, fields(item)) }))
    .filter((x): x is { item: T; score: number } => x.score !== null)
    .sort((a, b) => b.score - a.score)
    .map(x => x.item)
}

/** Flatten every schema's values and score them (matched on the value's label/name). */
function rankValues(schemas: Schema[] | undefined, query: string): { schema: Schema; value: SchemaValue; score: number }[] {
  if (!query.trim() || !schemas) return []
  const out: { schema: Schema; value: SchemaValue; score: number }[] = []
  for (const schema of schemas) {
    for (const value of schema.values) {
      const score = scoreFields(query, [value.label, value.name])
      if (score !== null) out.push({ schema, value, score })
    }
  }
  return out.sort((a, b) => b.score - a.score)
}
