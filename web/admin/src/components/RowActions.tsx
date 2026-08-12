import {
  Menu, MenuButton, MenuItem, MenuList, MenuPopover, MenuTrigger,
} from '@fluentui/react-components'
import { MoreVertical20Regular } from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

export interface RowAction {
  /** Unique key — used for React keys and aria attributes. */
  key: string
  /** Visible label in the menu. */
  label: string
  /** Optional icon (any 20x20 Fluent icon). */
  icon?: ReactNode
  /** Click handler. */
  onClick: () => void
  /** When true, the item is rendered with destructive styling. */
  destructive?: boolean
  /** Disable the item without hiding it. */
  disabled?: boolean
}

/**
 * Three-vertical-dot menu used as the last column in data grids.
 * Each action becomes a single menu item with optional leading icon and an optional destructive tint.
 */
export function RowActions({ actions, ariaLabel }: { actions: RowAction[]; ariaLabel?: string }) {
  const { t } = useTranslation()
  if (actions.length === 0) return null
  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <MenuButton
          appearance="subtle"
          icon={<MoreVertical20Regular />}
          aria-label={ariaLabel ?? t('shell.rowActions.ariaLabel')}
        />
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {actions.map(a => (
            <MenuItem
              key={a.key}
              icon={a.icon as never}
              disabled={a.disabled}
              onClick={a.onClick}
              style={a.destructive ? { color: 'var(--colorPaletteRedForeground1)' } : undefined}
            >
              {a.label}
            </MenuItem>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  )
}
