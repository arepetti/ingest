import { Link, NavLink, Outlet } from 'react-router-dom'
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components'
import { Board24Regular, CalendarLtr24Regular, ChartMultiple24Regular, DataTreemap24Regular, DocumentText24Regular, PeopleTeam24Regular, DocumentBulletList24Regular, History24Regular, Settings24Regular, Toolbox24Regular, Warning24Regular } from '@fluentui/react-icons'
import { useCapabilities } from '../api/hooks'
import { TopBar } from '../components/TopBar'
import type { ReactNode } from 'react'

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateColumns: '240px 1fr',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground1,
    position: 'relative',
  },
  // Off-screen until focused, then pinned top-left so keyboard users can jump straight to the
  // page body without tabbing through the whole sidebar on every navigation.
  skipLink: {
    position: 'absolute',
    left: '8px',
    top: '-48px',
    zIndex: 1000,
    padding: '8px 12px',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground1,
    border: `1px solid ${tokens.colorBrandStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    textDecoration: 'none',
    transition: 'top 0.1s ease-in',
    ':focus': { top: '8px' },
  },
  side: {
    backgroundColor: tokens.colorNeutralBackground3,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    flexDirection: 'column',
    padding: '16px 0',
  },
  brand: {
    padding: '8px 20px 16px',
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
  },
  brandTitle: {
    fontSize: '20px',
    fontWeight: 600,
  },
  brandSub: {
    fontSize: '12px',
    color: tokens.colorNeutralForeground3,
  },
  nav: { display: 'flex', flexDirection: 'column', gap: '2px', flex: 1 },
  // Pushes an entry (Settings) to the bottom of the sidebar, away from the main group.
  navItemBottom: { marginTop: 'auto' },
  navItem: {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    padding: '10px 20px',
    color: tokens.colorNeutralForeground2,
    borderLeft: '3px solid transparent',
    cursor: 'pointer',
    textDecoration: 'none',
  },
  navItemActive: {
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground1,
    borderLeftColor: tokens.colorBrandStroke1,
    fontWeight: 600,
  },
  // The right column is a flex container so the TopBar can be a non-scrolling header and the
  // body below it owns the scrollbar. Without this the whole page scrolls and the top bar drifts
  // out of view, defeating its purpose.
  mainColumn: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
    height: '100vh',
    overflow: 'hidden',
  },
  body: {
    flex: 1,
    overflow: 'auto',
    padding: '24px 32px',
  },
})

interface NavEntry {
  to: string
  label: string
  icon: ReactNode
  /** When false, the entry is hidden for the current role. */
  show: boolean
  end?: boolean
  /** When true, the entry is pinned to the bottom of the sidebar. */
  bottom?: boolean
}

export function Shell() {
  const s = useStyles()
  const { me, has, hasAny } = useCapabilities()

  const cls = ({ isActive }: { isActive: boolean }) =>
    isActive ? mergeClasses(s.navItem, s.navItemActive) : s.navItem

  // Each entry shows when the caller holds the capability backing its page; a pure self-service
  // account (no back-office capabilities) sees only the dashboard and its own submissions.
  const canConfigure = hasAny('settings:read', 'settings:manage', 'notifications:read', 'notifications:manage', 'webhooks:read', 'webhooks:manage')
  const navEntries: NavEntry[] = [
    { to: '/',            label: 'Dashboard',   icon: <Board24Regular />,              show: true, end: true },
    { to: '/submissions', label: 'Submissions', icon: <DocumentBulletList24Regular />, show: true },
    { to: '/missing',     label: 'Missing',     icon: <Warning24Regular />,            show: has('status:read') },
    { to: '/explore',     label: 'Explore',     icon: <ChartMultiple24Regular />,      show: has('explore:read') },
    { to: '/reports',     label: 'Reports',     icon: <DocumentText24Regular />,       show: has('reports:read') },
    // Route stays /services for URL stability (/services/{name}/status still resolves); the label
    // is "Accounts" because the page lists every account (any kind, any role), not only services.
    { to: '/services',    label: 'Accounts',    icon: <PeopleTeam24Regular />,         show: has('accounts:read') },
    { to: '/schemas',     label: 'Schemas',     icon: <DataTreemap24Regular />,        show: has('schemas:read') },
    { to: '/events',      label: 'Events',      icon: <CalendarLtr24Regular />,        show: has('events:read') },
    { to: '/audit',       label: 'Audit',       icon: <History24Regular />,            show: has('audit:read') },
    // Operational utilities (backup/restore today). Pinned to the bottom, directly above Settings.
    // `marginTop: auto` on the first bottom entry pushes the whole bottom group down.
    { to: '/tools',       label: 'Tools',       icon: <Toolbox24Regular />,            show: has('backup:read'), bottom: true },
    // Configuration hub (email, notifications, webhooks, default approval policy).
    { to: '/settings',    label: 'Settings',    icon: <Settings24Regular />,           show: canConfigure },
  ]

  // Subtitle reflects whether any back-office nav is visible at all.
  const isService = !navEntries.some(e => e.show && e.to !== '/' && e.to !== '/submissions')

  return (
    <div className={s.root}>
      <a href="#main-content" className={s.skipLink}>Skip to main content</a>
      <aside className={s.side} aria-label="Primary">
        <Link to="/" className={s.brand}>
          <span className={s.brandTitle}>Ingest</span>
          <span className={s.brandSub}>{isService ? 'Service console' : 'Admin console'}</span>
        </Link>

        <nav className={s.nav} aria-label="Main navigation">
          {navEntries.filter(e => e.show).map(e => (
            <NavLink
              key={e.to}
              to={e.to}
              end={e.end}
              className={state => mergeClasses(cls(state), e.bottom ? s.navItemBottom : undefined)}
            >
              {e.icon} {e.label}
            </NavLink>
          ))}
        </nav>
        {/* The account chip used to live here; it's been promoted to the TopBar where the
            account menu (sign-out) and the page breadcrumb sit together. */}
      </aside>
      <div className={s.mainColumn}>
        <TopBar me={me} />
        <main id="main-content" className={s.body} tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}
