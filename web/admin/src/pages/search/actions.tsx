import type { ReactNode } from 'react'
import {
  Add20Regular, ArrowUpload20Regular, Board20Regular, CalendarLtr20Regular,
  ChartMultiple20Regular, DataTreemap20Regular, DocumentBulletList20Regular,
  DocumentText20Regular, History20Regular, PeopleTeam20Regular, PersonAdd20Regular,
  Settings20Regular, Toolbox20Regular, Warning20Regular,
} from '@fluentui/react-icons'
import type { Capability } from '../../api/capabilities'

/**
 * A task/action shortcut the global search can surface. This is a purely client-side catalogue:
 * typing an intent ("add user", "explore", "rag") resolves to a route — either a plain page or,
 * where the target supports it, a direct action (a "New …" route or a page opened straight onto a
 * create dialog / tab via query params).
 */
export interface SearchAction {
  id: string
  title: string
  /** One-line hint shown under the title in the results. */
  description: string
  /** Extra terms (synonyms/intents) matched in addition to the title. */
  keywords: string[]
  /** Destination route (may include a query string for a direct action). */
  to: string
  /** Optional explicit icon; when omitted an id-keyed default from `actionIcon` is used. */
  icon?: ReactNode
  /**
   * Any-of capability gate: the action is shown when the caller holds at least one of these.
   * Omit for actions available to everyone who can reach the console (e.g. the dashboard).
   */
  capabilities?: Capability[]
}

/**
 * The action catalogue, roughly ordered by how "actionable" each entry is (create/do first, then
 * navigate). Ordering here is only a tie-breaker; relevance scoring against the query dominates.
 */
export const ACTIONS: SearchAction[] = [
  // --- Create / do -------------------------------------------------------------------------
  {
    id: 'submission-new',
    title: 'New submission',
    description: 'Submit KPI data for a service',
    keywords: ['submit', 'add submission', 'create submission', 'send data', 'report data'],
    to: '/submissions/new',
  },
  {
    id: 'schema-new',
    title: 'Add schema',
    description: 'Create a new schema',
    keywords: ['new schema', 'create schema', 'add kpi'],
    to: '/schemas/new',
    capabilities: ['schemas:manage'],
  },
  {
    id: 'account-new-user',
    title: 'Add user',
    description: 'Create a new account',
    keywords: ['new user', 'create user', 'add account', 'new account', 'add operator', 'add admin', 'invite'],
    to: '/services?new=1',
    capabilities: ['accounts:manage'],
  },
  {
    id: 'account-new-service',
    title: 'Add service',
    description: 'Create a new service account',
    keywords: ['new service', 'create service', 'onboard service', 'add account'],
    to: '/services?new=1',
    capabilities: ['accounts:manage'],
  },
  {
    id: 'event-new',
    title: 'Add event',
    description: 'Record a timeline event',
    keywords: ['new event', 'create event', 'incident', 'maintenance', 'deployment'],
    to: '/events?new=1',
    capabilities: ['events:manage'],
  },
  {
    id: 'report-upload',
    title: 'Upload report',
    description: 'Add a report definition',
    keywords: ['new report', 'add report', 'import report'],
    to: '/reports',
    capabilities: ['reports:manage'],
  },
  // --- Analytics ---------------------------------------------------------------------------
  {
    id: 'explore',
    title: 'Explore',
    description: 'In-app analytics: trends, compare, snapshot',
    keywords: ['analytics', 'analyse', 'analyze', 'chart', 'trend', 'graph'],
    to: '/explore',
    capabilities: ['explore:read'],
  },
  {
    id: 'explore-scorecard',
    title: 'Scorecard',
    description: 'Cross-schema RAG status board',
    keywords: ['rag', 'red amber green', 'traffic light', 'status board', 'targets'],
    to: '/explore?tab=scorecard',
    capabilities: ['explore:read'],
  },
  {
    id: 'explore-anomalies',
    title: 'Anomalies',
    description: 'Per-period anomaly detection board',
    keywords: ['anomaly', 'outlier', 'unusual', 'spike'],
    to: '/explore?tab=anomalies',
    capabilities: ['explore:read'],
  },
  {
    id: 'missing',
    title: 'Missing submissions',
    description: 'What each service still owes',
    keywords: ['missing', 'overdue', 'gaps', 'not submitted'],
    to: '/missing',
    capabilities: ['status:read'],
  },
  // --- Navigate ----------------------------------------------------------------------------
  {
    id: 'dashboard',
    title: 'Dashboard',
    description: 'Home overview',
    keywords: ['home', 'overview', 'start'],
    to: '/',
    icon: <Board20Regular />,
  },
  {
    id: 'submissions',
    title: 'Submissions',
    description: 'Browse submitted data',
    keywords: ['data', 'kpi', 'samples'],
    to: '/submissions',
  },
  {
    id: 'schemas',
    title: 'Schemas',
    description: 'Browse the schema catalogue',
    keywords: ['kpi definitions', 'templates', 'fields'],
    to: '/schemas',
    capabilities: ['schemas:read'],
  },
  {
    id: 'accounts',
    title: 'Accounts',
    description: 'Services, operators, users and admins',
    keywords: ['services', 'users', 'operators', 'admins', 'people'],
    to: '/services',
    capabilities: ['accounts:read'],
  },
  {
    id: 'events',
    title: 'Events',
    description: 'Browse the events timeline',
    keywords: ['timeline', 'incidents', 'maintenance'],
    to: '/events',
    capabilities: ['events:read'],
  },
  {
    id: 'reports',
    title: 'Reports',
    description: 'Browse and render reports',
    keywords: ['report catalogue', 'render'],
    to: '/reports',
    capabilities: ['reports:read'],
  },
  {
    id: 'audit',
    title: 'Audit',
    description: 'Who changed what, when',
    keywords: ['audit log', 'history', 'trail', 'changes'],
    to: '/audit',
    capabilities: ['audit:read'],
  },
  {
    id: 'tools',
    title: 'Tools',
    description: 'Backup and restore utilities',
    keywords: ['backup', 'restore', 'export', 'import'],
    to: '/tools',
    capabilities: ['backup:read'],
  },
  {
    id: 'settings',
    title: 'Settings',
    description: 'Email, notifications, webhooks and policy',
    keywords: ['configuration', 'config', 'email', 'notifications', 'webhooks', 'approval policy'],
    to: '/settings',
    capabilities: ['settings:read', 'settings:manage', 'notifications:read', 'notifications:manage', 'webhooks:read', 'webhooks:manage'],
  },
]

// Default icons for actions that don't set one explicitly, keyed by id so the catalogue above stays
// terse. Anything without an entry here falls back to a generic "action" glyph.
const ICONS: Record<string, ReactNode> = {
  'submission-new': <DocumentBulletList20Regular />,
  'schema-new': <Add20Regular />,
  'account-new-user': <PersonAdd20Regular />,
  'account-new-service': <PersonAdd20Regular />,
  'event-new': <Add20Regular />,
  'report-upload': <ArrowUpload20Regular />,
  explore: <ChartMultiple20Regular />,
  'explore-scorecard': <ChartMultiple20Regular />,
  'explore-anomalies': <ChartMultiple20Regular />,
  missing: <Warning20Regular />,
  submissions: <DocumentBulletList20Regular />,
  schemas: <DataTreemap20Regular />,
  accounts: <PeopleTeam20Regular />,
  events: <CalendarLtr20Regular />,
  reports: <DocumentText20Regular />,
  audit: <History20Regular />,
  tools: <Toolbox20Regular />,
  settings: <Settings20Regular />,
}

/** The icon to render for an action (its explicit icon, else the id-keyed default). */
export function actionIcon(a: SearchAction): ReactNode {
  return a.icon ?? ICONS[a.id] ?? <Add20Regular />
}

/** Split a query into lowercased, non-empty tokens. */
function tokenize(q: string): string[] {
  return q.toLowerCase().split(/\s+/).filter(Boolean)
}

/**
 * Score a set of text fields against a query. Returns `null` when it doesn't match (not every
 * query token appears across the fields), otherwise a higher-is-better relevance score. The first
 * field is treated as the primary (title/name) and earns the position bonuses.
 */
export function scoreFields(query: string, fields: (string | null | undefined)[]): number | null {
  const qs = tokenize(query)
  if (qs.length === 0) return null
  const parts = fields.filter((f): f is string => !!f).map(f => f.toLowerCase())
  if (parts.length === 0) return null
  const haystack = parts.join(' ')
  // Every token must be present somewhere, so multi-word queries narrow rather than widen.
  if (!qs.every(t => haystack.includes(t))) return null

  const primary = parts[0]
  const full = query.toLowerCase().trim()
  let score = 0
  if (primary === full) score += 100
  else if (primary.startsWith(full)) score += 60
  else if (primary.includes(full)) score += 40
  // Reward how much of the primary field the query tokens cover, and prefer shorter primaries.
  score += qs.filter(t => primary.includes(t)).length * 5
  score += Math.max(0, 20 - primary.length / 4)
  return score
}

/**
 * Match the action catalogue against a query, filtered to what the caller can use. Results are
 * ordered by relevance (then title) and capped so the actions block stays scannable.
 */
export function matchActions(query: string, canUse: (a: SearchAction) => boolean): SearchAction[] {
  if (!query.trim()) return []
  return ACTIONS
    .filter(canUse)
    .map(a => ({ a, score: scoreFields(query, [a.title, ...a.keywords]) }))
    .filter((x): x is { a: SearchAction; score: number } => x.score !== null)
    .sort((x, y) => y.score - x.score || x.a.title.localeCompare(y.a.title))
    .slice(0, 8)
    .map(x => x.a)
}
