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

  return (
    <div className={s.root}>
      <Title2>{title}</Title2>
      <div className={s.split}>
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
        <div className={s.content}>{active?.render()}</div>
      </div>
    </div>
  )
}

// Re-export so consumers can colocate the section background token if they need it.
export const sectionBorderColor = tokens.colorNeutralStroke2
