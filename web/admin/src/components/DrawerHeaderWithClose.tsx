import {
  Button, DrawerHeader, DrawerHeaderTitle,
} from '@fluentui/react-components'
import {
  ArrowMaximize20Regular, ArrowMinimize20Regular, Dismiss20Regular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * Standard drawer header with the close ("X") affordance, plus an optional expand/collapse
 * toggle to enlarge the drawer to the full viewport width (handy when editing big schemas or
 * reviewing dense submission payloads).
 *
 * `expanded` + `onToggleExpand` are optional — when both are omitted the header renders just
 * the close button so existing call sites stay unchanged.
 */
export function DrawerHeaderWithClose({
  title,
  onClose,
  expanded,
  onToggleExpand,
}: {
  title: ReactNode
  onClose: () => void
  /** Current expanded state. Required only if `onToggleExpand` is supplied. */
  expanded?: boolean
  /** Toggle handler; when supplied the expand button is rendered before the close button. */
  onToggleExpand?: () => void
}) {
  const { t } = useTranslation()
  const showExpand = typeof onToggleExpand === 'function'
  return (
    <DrawerHeader>
      <DrawerHeaderTitle
        action={
          <span style={{ display: 'inline-flex', gap: '4px' }}>
            {showExpand && (
              <Button
                appearance="transparent"
                icon={expanded ? <ArrowMinimize20Regular /> : <ArrowMaximize20Regular />}
                aria-label={expanded
                  ? t('shell.drawer.collapse')
                  : t('shell.drawer.expand')}
                onClick={onToggleExpand}
              />
            )}
            <Button
              appearance="transparent"
              icon={<Dismiss20Regular />}
              aria-label={t('shell.common.close')}
              onClick={onClose}
            />
          </span>
        }
      >
        {title}
      </DrawerHeaderTitle>
    </DrawerHeader>
  )
}

/**
 * Shared style override for `OverlayDrawer` widths. The defaults Fluent ships with feel a
 * little narrow for our schema/submission editors; the constants below keep every page in sync.
 */
export const DRAWER_DEFAULT_WIDTH = 'max(600px, 50vw)'
export const DRAWER_EXPANDED_WIDTH = '100vw'
