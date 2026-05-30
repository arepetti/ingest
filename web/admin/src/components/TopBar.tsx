import { useMemo } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import {
  Avatar, Breadcrumb, BreadcrumbButton, BreadcrumbDivider, BreadcrumbItem,
  Button, Menu, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Text, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import { ChevronDown16Regular, SignOut20Regular } from '@fluentui/react-icons'
import { setApiKey } from '../api/client'
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

  // Service-role users can't hit the admin listings — gate the lookups so we don't spam 403s
  // just to populate breadcrumbs they'd never see anyway. Their breadcrumbs only ever feature
  // /, /submissions, /submissions/new and /submissions/:id, none of which need an entity label.
  const isService = me?.role === 'Service'
  const accountsQuery = useAccounts(undefined, !isService)
  const schemasQuery = useSchemas(undefined, !isService)
  // Reports are an operator/admin tool; same gating logic.
  const reportsQuery = useReports(!isService)

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

  function logout() {
    setApiKey(null)
    nav('/login', { replace: true })
  }

  const crumbs = buildBreadcrumbs(pathname, labels)
  const displayName = me?.label || me?.name || ''

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
