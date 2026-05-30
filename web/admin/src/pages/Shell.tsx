import { Link, NavLink, Outlet } from 'react-router-dom'
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components'
import { Board24Regular, DataTreemap24Regular, DocumentText24Regular, PeopleTeam24Regular, DocumentBulletList24Regular } from '@fluentui/react-icons'
import { useMe } from '../api/hooks'
import { TopBar } from '../components/TopBar'
import type { ReactNode } from 'react'

const useStyles = makeStyles({
  root: {
    display: 'grid',
    gridTemplateColumns: '240px 1fr',
    height: '100vh',
    backgroundColor: tokens.colorNeutralBackground1,
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
}

export function Shell() {
  const s = useStyles()
  const { data: me } = useMe()

  const cls = ({ isActive }: { isActive: boolean }) =>
    isActive ? mergeClasses(s.navItem, s.navItemActive) : s.navItem

  // Services see a stripped-down sidebar: they only need a dashboard and access to their own submissions.
  // Schemas/services management pages call admin endpoints and would just 403 for them.
  const isService = me?.role === 'Service'
  const navEntries: NavEntry[] = [
    { to: '/',            label: 'Dashboard',   icon: <Board24Regular />,              show: true, end: true },
    { to: '/schemas',     label: 'Schemas',     icon: <DataTreemap24Regular />,        show: !isService },
    // Route stays /services for URL stability (/services/{name}/status still resolves); the label
    // is "Accounts" because the page lists every account (any kind, any role), not only services.
    { to: '/services',    label: 'Accounts',    icon: <PeopleTeam24Regular />,         show: !isService },
    { to: '/submissions', label: 'Submissions', icon: <DocumentBulletList24Regular />, show: true },
    // Reports are an operator/admin tool; service-role users would get a 403 for the catalogue
    // and have no reason to use them anyway.
    { to: '/reports',     label: 'Reports',     icon: <DocumentText24Regular />,       show: !isService },
  ]

  return (
    <div className={s.root}>
      <aside className={s.side}>
        <Link to="/" className={s.brand}>
          <span className={s.brandTitle}>Ingest</span>
          <span className={s.brandSub}>{isService ? 'Service console' : 'Admin console'}</span>
        </Link>

        <nav className={s.nav}>
          {navEntries.filter(e => e.show).map(e => (
            <NavLink key={e.to} to={e.to} end={e.end} className={cls}>
              {e.icon} {e.label}
            </NavLink>
          ))}
        </nav>
        {/* The account chip used to live here; it's been promoted to the TopBar where the
            account menu (sign-out) and the page breadcrumb sit together. */}
      </aside>
      <div className={s.mainColumn}>
        <TopBar me={me} />
        <main className={s.body}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}
