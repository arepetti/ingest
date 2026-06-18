import { useState } from 'react'
import type { ReactNode } from 'react'
import { Tab, TabList, Title2, makeStyles, tokens } from '@fluentui/react-components'

/**
 * One entry in a {@link SectionedLayout}. The `render` callback is only invoked while the
 * section is selected, so each panel mounts lazily and unmounts when you navigate away — handy
 * for forms that keep their own draft state (selecting a section again starts fresh).
 */
export interface LayoutSection {
  id: string
  label: string
  icon?: ReactNode
  /**
   * Optional category. When any section carries a group, the nav is split into one labelled
   * vertical list per group (VS Code's settings layout). Sections without a group fall back to the
   * single flat list.
   */
  group?: string
  render: () => ReactNode
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px', minWidth: 0 },
  // Master (vertical nav) on the left, detail pane on the right — VS Code's settings layout.
  split: { display: 'flex', gap: '28px', alignItems: 'flex-start', minWidth: 0 },
  nav: {
    flex: '0 0 auto',
    minWidth: '200px',
    position: 'sticky',
    top: 0,
  },
  // Grouped nav: stacked labelled tab-lists.
  navGroups: { display: 'flex', flexDirection: 'column', gap: '14px' },
  groupLabel: {
    fontWeight: 600,
    fontSize: '12px',
    color: tokens.colorNeutralForeground2,
    textTransform: 'uppercase',
    letterSpacing: '0.02em',
    padding: '0 0 2px 10px',
  },
  content: { flex: 1, minWidth: 0 },
})

/**
 * A master-detail page scaffold: a sticky vertical list of sections on the left and the selected
 * section's content on the right. Built on Fluent's vertical `TabList`, so keyboard navigation
 * and ARIA roles come for free. Used by the Settings and Tools pages.
 */
export function SectionedLayout({
  title,
  sections,
  initialSectionId,
}: {
  title: string
  sections: LayoutSection[]
  /** Section to open first; defaults to the first entry. */
  initialSectionId?: string
}) {
  const s = useStyles()
  const [selected, setSelected] = useState(initialSectionId ?? sections[0]?.id)
  const active = sections.find(x => x.id === selected) ?? sections[0]

  const grouped = sections.some(sec => sec.group)
  // Distinct group names in first-appearance order.
  const groups = grouped
    ? sections.reduce<string[]>((acc, sec) => {
        const g = sec.group ?? ''
        if (!acc.includes(g)) acc.push(g)
        return acc
      }, [])
    : []

  return (
    <div className={s.root}>
      <Title2>{title}</Title2>
      <div className={s.split}>
        {grouped ? (
          <div className={s.nav}>
            <div className={s.navGroups}>
              {groups.map(g => (
                <div key={g || '_'}>
                  {g && <div className={s.groupLabel}>{g}</div>}
                  <TabList
                    vertical
                    // Always controlled by the single shared selection. Groups that don't contain
                    // the active section simply match none of their tabs, so only one tab is
                    // highlighted across all groups. (Passing `undefined` here would make the list
                    // uncontrolled and let each group highlight a tab of its own.)
                    selectedValue={active?.id}
                    onTabSelect={(_, d) => setSelected(d.value as string)}
                  >
                    {sections.filter(sec => (sec.group ?? '') === g).map(sec => (
                      <Tab key={sec.id} value={sec.id} icon={sec.icon as never}>{sec.label}</Tab>
                    ))}
                  </TabList>
                </div>
              ))}
            </div>
          </div>
        ) : (
          <TabList
            className={s.nav}
            vertical
            selectedValue={active?.id}
            onTabSelect={(_, d) => setSelected(d.value as string)}
          >
            {sections.map(sec => (
              <Tab key={sec.id} value={sec.id} icon={sec.icon as never}>{sec.label}</Tab>
            ))}
          </TabList>
        )}
        <div className={s.content}>{active?.render()}</div>
      </div>
    </div>
  )
}
