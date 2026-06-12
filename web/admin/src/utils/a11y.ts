import type { KeyboardEvent } from 'react'

/**
 * Spread these onto a non-interactive element (typically a data-grid `<TableRow>`) whose whole
 * surface acts as a "click to open" shortcut, so the affordance is reachable and operable by
 * keyboard — not just the mouse.
 *
 * - `tabIndex={0}` puts the row in the tab order.
 * - `onKeyDown` activates on **Enter** / **Space**, but only when the row itself is focused
 *   (`e.target === e.currentTarget`). Events bubbling up from inner controls — the actions menu,
 *   links, buttons — are ignored so those keep their own keyboard behaviour and the row doesn't
 *   double-fire.
 * - `aria-label` gives the row an accessible name announced on focus (the cells alone don't say
 *   what activating the row does).
 *
 * The row keeps its native table-row semantics on purpose (no `role` override) so screen-reader
 * table navigation still works; this is a progressive enhancement layered on top.
 */
export function clickableRowProps(onActivate: () => void, ariaLabel?: string) {
  return {
    tabIndex: 0,
    'aria-label': ariaLabel,
    onClick: onActivate,
    onKeyDown: (e: KeyboardEvent) => {
      if (e.target !== e.currentTarget) return
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault()
        onActivate()
      }
    },
  }
}
