import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import {
  Avatar, Badge, Breadcrumb, BreadcrumbButton, BreadcrumbDivider, BreadcrumbItem,
  Button, Menu, MenuItem, MenuList, MenuPopover, MenuTrigger,
  SearchBox, Text, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import { ChevronDown16Regular, SignOut20Regular } from '@fluentui/react-icons'
import { api, setApiKey } from '../api/client'
import { useAccounts, useReports, useSchemas } from '../api/hooks'
import type { Me } from '../api/types'

const useStyles = makeStyles({
  // The bar is hosted inside a flex column main area whose body has overflow:auto, so it stays
  // permanently in view without needing position:fixed (and without the layout headaches that
  // come with it — width, scrollbar offsets, sidebar collision).
  root: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '16px',
    height: '52px',
    padding: '0 24px',
    backgroundColor: tokens.colorNeutralBackground3,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Breadcrumb sometimes contains a long entity name (a schema label, a service name) — let it
  // shrink and ellipsise rather than push the account button off-screen.
  crumbs: { minWidth: 0, overflow: 'hidden' },
  account: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
  },
  // Global search box, sitting just left of the account menu. Submit on Enter.
  searchBox: { width: '220px', maxWidth: '32vw' },
  // Avatar + name + chevron as a single subtle button. Padding kept tight so the bar reads as a
  // header strip rather than a toolbar.
  accountButton: {
    minWidth: 'auto',
    padding: '4px 8px',
    height: '40px',
  },
  accountName: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    lineHeight: 1.1,
    textAlign: 'left',
  },
  accountRole: {
    fontSize: '11px',
    color: tokens.colorNeutralForeground3,
  },
})

interface Crumb {
  label: string
  to?: string
}

/**
 * Map the current pathname (with its url params) into a breadcrumb trail. Pure string-matching
 * keeps the component self-contained: pages don't need to register anything to participate.
 *
 * For dynamic segments (`:name`, `:id`) the machine-style URL value is replaced with the
 * friendly entity label when the lookup map has a hit — the same labels users see in the data
 * grids. Falls back to the raw URL segment when the lookup map isn't populated yet (first
 * paint before TanStack Query resolves) so the bar always shows something stable.
 *
 * Submission ids stay as the generic "Submission" label — submissions don't carry a name we
 * could surface without an extra fetch per breadcrumb render.
 */
function buildBreadcrumbs(pathname: string, labels: LabelLookups): Crumb[] {
  // The "Home" anchor is always the first crumb. Even on `/` itself we keep it so the bar's
  // structure is consistent across every page — there it just renders as the current crumb.
  const home: Crumb = { label: 'Home', to: '/' }
  const schemaLabel = (name: string) => labels.schemas[name] || decodeURIComponent(name)
  const accountLabel = (name: string) => labels.accounts[name] || decodeURIComponent(name)
  const reportLabel = (name: string) => labels.reports[name] || decodeURIComponent(name)

  // Order matters — match the most specific route first.
  const rules: Array<{ re: RegExp; build: (m: RegExpMatchArray) => Crumb[] }> = [
    { re: /^\/$/, build: () => [{ label: 'Home' }] },

    { re: /^\/schemas$/, build: () => [home, { label: 'Schemas' }] },
    {
      re: /^\/schemas\/new$/,
      build: () => [home, { label: 'Schemas', to: '/schemas' }, { label: 'New' }],
    },
    {
      re: /^\/schemas\/([^/]+)\/edit$/,
      build: (m) => [
        home,
        { label: 'Schemas', to: '/schemas' },
        { label: schemaLabel(m[1]) },
        { label: 'Edit' },
      ],
    },
    {
      re: /^\/schemas\/([^/]+)\/history$/,
      build: (m) => [
        home,
        { label: 'Schemas', to: '/schemas' },
        { label: schemaLabel(m[1]) },
        { label: 'Historical data' },
      ],
    },

    { re: /^\/services$/, build: () => [home, { label: 'Accounts' }] },
    {
      re: /^\/services\/([^/]+)\/status$/,
      build: (m) => [
        home,
        { label: 'Accounts', to: '/services' },
        { label: accountLabel(m[1]) },
        { label: 'Status' },
      ],
    },

    { re: /^\/submissions$/, build: () => [home, { label: 'Submissions' }] },
    {
      re: /^\/submissions\/new$/,
      build: () => [home, { label: 'Submissions', to: '/submissions' }, { label: 'New' }],
    },
    {
      re: /^\/submissions\/([^/]+)\/edit$/,
      build: (m) => [
        home,
        { label: 'Submissions', to: '/submissions' },
        { label: 'Submission', to: `/submissions/${encodeURIComponent(m[1])}` },
        { label: 'Edit' },
      ],
    },
    {
      re: /^\/submissions\/([^/]+)\/view$/,
      build: (m) => [
        home,
        { label: 'Submissions', to: '/submissions' },
        { label: 'Submission', to: `/submissions/${encodeURIComponent(m[1])}` },
        { label: 'View' },
      ],
    },
    {
      re: /^\/submissions\/([^/]+)$/,
      build: () => [home, { label: 'Submissions', to: '/submissions' }, { label: 'Submission' }],
    },

    { re: /^\/reports$/, build: () => [home, { label: 'Reports' }] },
    {
      re: /^\/reports\/([^/]+)$/,
      build: (m) => [
        home,
        { label: 'Reports', to: '/reports' },
        { label: reportLabel(m[1]) },
      ],
    },

    { re: /^\/audit$/, build: () => [home, { label: 'Audit' }] },

    { re: /^\/missing$/, build: () => [home, { label: 'Missing submissions' }] },
    { re: /^\/explore$/, build: () => [home, { label: 'Explore' }] },
    { re: /^\/tools$/, build: () => [home, { label: 'Tools' }] },
    { re: /^\/settings$/, build: () => [home, { label: 'Settings' }] },
  ]

  for (const r of rules) {
    const m = pathname.match(r.re)
    if (m) return r.build(m)
  }
  // Unknown route — surface "Home" alone so the bar isn't empty.
  return [{ label: 'Home' }]
}

/** Name → friendly label maps used to rewrite dynamic breadcrumb segments. */
interface LabelLookups {
  schemas: Record<string, string>
  accounts: Record<string, string>
  reports: Record<string, string>
}

/**
 * Sticky strip above the page body: breadcrumb on the left, account menu on the right.
 * Stays visible while the page body scrolls because the main column is a flex container whose
 * body owns the overflow (see Shell.tsx).
 */
export function TopBar({ me }: { me?: Me }) {
  const s = useStyles()
  const nav = useNavigate()
  const { pathname } = useLocation()

  // Global search: submitting (Enter or the search button) navigates to the results page and empties
  // the box, so it's always a fresh entry point rather than lingering with the last query. Ctrl/Cmd+K
  // focuses it from anywhere in the console.
  const [term, setTerm] = useState('')
  const searchRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault()
        searchRef.current?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])
  function submitSearch() {
    const query = term.trim()
    nav(`/search${query ? `?q=${encodeURIComponent(query)}` : ''}`)
    setTerm('')
  }

  // Gate each breadcrumb-label lookup by the capability backing its listing so we don't spam 403s
  // for callers who can't read that entity (their breadcrumbs never feature those routes anyway).
  const caps = new Set(me?.capabilities ?? [])
  const accountsQuery = useAccounts(undefined, caps.has('accounts:read'))
  const schemasQuery = useSchemas(undefined, caps.has('schemas:read'))
  const reportsQuery = useReports(undefined, caps.has('reports:read'))

  // Memoise the name → label maps so buildBreadcrumbs gets stable inputs and isn't pointlessly
  // rerun while React Query mutates the parent objects between requests.
  const labels = useMemo<LabelLookups>(() => {
    const accounts: Record<string, string> = {}
    for (const a of accountsQuery.data?.items ?? []) {
      if (a.label) accounts[a.name] = a.label
    }
    const schemas: Record<string, string> = {}
    for (const sc of schemasQuery.data?.items ?? []) {
      if (sc.label) schemas[sc.name] = sc.label
    }
    const reports: Record<string, string> = {}
    for (const r of reportsQuery.data?.items ?? []) {
      if (r.label) reports[r.name] = r.label
    }
    return { accounts, schemas, reports }
  }, [accountsQuery.data, schemasQuery.data, reportsQuery.data])

  async function logout() {
    // Clear the SSO session cookie server-side (no-op when SSO is off / no cookie), then drop any
    // local API key and bounce to the login screen. Best-effort: navigate even if the call fails.
    try {
      await api.post<void>('/api/auth/logout')
    } catch { /* ignore — we're signing out regardless */ }
    setApiKey(null)
    nav('/login', { replace: true })
  }

  const crumbs = buildBreadcrumbs(pathname, labels)
  const displayName = me?.label || me?.name || ''

  // When the session is confined to a subset of services, surface a badge so the operator always
  // knows they're seeing a limited view (and not mistake a partial dataset for the whole estate).
  const scopeIds = me?.assignedServiceIds ?? []
  const scoped = scopeIds.length > 0
  const scopeNames = scopeIds
    .map(id => {
      const a = (accountsQuery.data?.items ?? []).find(x => x.id === id)
      return a ? (a.label || a.name) : null
    })
    .filter((n): n is string => !!n)
  const scopeTooltip = scopeNames.length > 0
    ? `This session can only see: ${scopeNames.join(', ')}`
    : `This session is limited to ${scopeIds.length} service${scopeIds.length === 1 ? '' : 's'}.`

  return (
    <header className={s.root}>
      <div className={s.crumbs}>
        <Breadcrumb aria-label="Page location" size="medium">
          {crumbs.map((c, i) => {
            const isLast = i === crumbs.length - 1
            return (
              <BreadcrumbCrumb
                key={`${c.label}-${i}`}
                crumb={c}
                current={isLast}
                showDivider={i > 0}
                onNavigate={(to) => nav(to)}
              />
            )
          })}
        </Breadcrumb>
      </div>

      <div className={s.account}>
        <SearchBox
          ref={searchRef}
          className={s.searchBox}
          placeholder="Search…"
          aria-label="Search (Ctrl+K)"
          value={term}
          onChange={(_, d) => setTerm(d.value)}
          onKeyDown={e => { if (e.key === 'Enter') submitSearch() }}
        />
        {scoped && (
          <Tooltip content={scopeTooltip} relationship="description" positioning="below">
            <Badge appearance="tint" color="brand">
              Limited view · {scopeIds.length} service{scopeIds.length === 1 ? '' : 's'}
            </Badge>
          </Tooltip>
        )}
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            {/* Tooltip carries the role: the visible bar mentions only label/name so it doesn't
                feel cluttered, but the role is one hover away. */}
            <Tooltip
              content={me?.role ? `Role: ${me.role}` : 'Account'}
              relationship="description"
              positioning="below-end"
            >
              <Button
                appearance="subtle"
                className={s.accountButton}
                icon={<ChevronDown16Regular />}
                iconPosition="after"
                aria-label={displayName ? `Account menu for ${displayName}` : 'Account menu'}
              >
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                  <Avatar name={displayName || 'Account'} size={28} />
                  <span className={s.accountName}>
                    <Text weight="semibold" size={200}>{displayName || '...'}</Text>
                    {me?.role ? <span className={s.accountRole}>{me.role}</span> : null}
                  </span>
                </span>
              </Button>
            </Tooltip>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem icon={<SignOut20Regular />} onClick={logout}>Sign out</MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>
    </header>
  )
}

/**
 * Renders one breadcrumb position. Navigable crumbs route through the parent's
 * <c>onNavigate</c> callback so we stay inside the router's history stack; the current crumb
 * is rendered with <c>current</c> so Fluent shows it without hover/focus chrome.
 * <c>BreadcrumbDivider</c> is inserted before every non-first crumb.
 */
function BreadcrumbCrumb({
  crumb, current, showDivider, onNavigate,
}: {
  crumb: Crumb; current: boolean; showDivider: boolean; onNavigate: (to: string) => void
}) {
  return (
    <>
      {showDivider && <BreadcrumbDivider />}
      <BreadcrumbItem>
        {crumb.to && !current ? (
          <BreadcrumbButton onClick={() => onNavigate(crumb.to!)}>
            {crumb.label}
          </BreadcrumbButton>
        ) : (
          <BreadcrumbButton current={current}>{crumb.label}</BreadcrumbButton>
        )}
      </BreadcrumbItem>
    </>
  )
}
