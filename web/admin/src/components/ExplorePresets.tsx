import { useState } from 'react'
import {
  Button, Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular, BookmarkMultiple20Regular, Delete16Regular } from '@fluentui/react-icons'

/** Where saved Explore views live, and how many we keep. Browser-local, per-browser only. */
const STORAGE_KEY = 'ingest.explore.presets'
const MAX_PRESETS = 5

/** A saved Explore view: a name plus the full URL query string it maps to. */
interface Preset {
  name: string
  query: string
}

function loadPresets(): Preset[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw)
    if (!Array.isArray(parsed)) return []
    return parsed
      .filter((p): p is Preset => !!p && typeof p.name === 'string' && typeof p.query === 'string')
      .slice(0, MAX_PRESETS)
  } catch {
    return []
  }
}

function savePresets(presets: Preset[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(presets))
}

const useStyles = makeStyles({
  item: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' },
  itemName: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '200px' },
  empty: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, padding: '4px 10px' },
})

/**
 * Small "saved views" control for Explore: a dropdown listing up to five presets (each loadable, or
 * deletable via a trailing trash icon with no confirmation) and, after a separator, an action to
 * save the current filter/setting selection under a typed name. Presets are kept in localStorage and
 * store the page's full URL query string, so every current and future filter round-trips for free.
 */
export function ExplorePresets({ current, onLoad }: { current: string; onLoad: (query: string) => void }) {
  const s = useStyles()
  const [presets, setPresets] = useState<Preset[]>(loadPresets)

  const persist = (next: Preset[]) => {
    setPresets(next)
    savePresets(next)
  }

  const remove = (name: string) => {
    persist(presets.filter(p => p.name !== name))
  }

  const saveCurrent = () => {
    const name = window.prompt('Preset name')?.trim()
    if (!name) return
    const without = presets.filter(p => p.name !== name)
    if (without.length >= MAX_PRESETS) {
      window.alert(`You can keep at most ${MAX_PRESETS} presets. Delete one first.`)
      return
    }
    persist([...without, { name, query: current }])
  }

  const atLimit = presets.length >= MAX_PRESETS

  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <MenuButton appearance="subtle" size="small" icon={<BookmarkMultiple20Regular />}>Presets</MenuButton>
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {presets.length === 0 ? (
            <div className={s.empty}>No saved presets yet.</div>
          ) : (
            presets.map(p => (
              <MenuItem key={p.name} onClick={() => onLoad(p.query)}>
                <div className={s.item}>
                  <span className={s.itemName}>{p.name}</span>
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<Delete16Regular />}
                    aria-label={`Delete preset ${p.name}`}
                    onClick={e => { e.stopPropagation(); remove(p.name) }}
                  />
                </div>
              </MenuItem>
            ))
          )}
          <MenuDivider />
          <MenuItem icon={<Add20Regular />} onClick={saveCurrent} disabled={atLimit}>
            Save current as preset…
          </MenuItem>
        </MenuList>
      </MenuPopover>
    </Menu>
  )
}
