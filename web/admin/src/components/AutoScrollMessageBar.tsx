import { useEffect, useRef, type ReactNode } from 'react'
import { MessageBar, makeStyles, type MessageBarProps } from '@fluentui/react-components'

const useStyles = makeStyles({
  // Preserve newlines that come from server-formatted error lists (e.g. a 400 with several
  // validation errors joined with "\n" in `ApiError.detail`). `pre-line` keeps the newlines
  // but still collapses runs of whitespace, so prose-style messages render normally too.
  wrap: { whiteSpace: 'pre-line' },
})

/**
 * Drop-in replacement for Fluent UI's `MessageBar` that scrolls itself into view whenever the
 * bar first appears or its rendered text changes. Use it for actionable notices — submission
 * errors, "could not save" warnings, the rotated-API-key disclosure — that may end up above or
 * below the fold relative to whichever button the user just clicked. Without auto-scroll the
 * user keeps hammering "Save" without realising the form already rejected.
 *
 * The scroll is intentionally cheap: it fires only when the *flattened text* of the children
 * actually changes, so identical errors raised twice in a row (e.g. retry → same response) don't
 * cause a second smooth-scroll jump that feels jittery on touchpads.
 */
export function AutoScrollMessageBar({ children, ...rest }: MessageBarProps) {
  const s = useStyles()
  const ref = useRef<HTMLDivElement>(null)
  // Remember the last key we scrolled for. The first render always scrolls because `null !== key`.
  const lastKey = useRef<string | null>(null)

  useEffect(() => {
    if (!ref.current) return
    const key = `${rest.intent ?? 'info'}::${flattenText(children)}`
    if (lastKey.current === key) return
    lastKey.current = key
    // `block: 'center'` keeps the bar in view even when it was already partially on screen,
    // which is the common case after clicking a sticky toolbar button. `behavior: 'smooth'`
    // is honoured-or-instant based on the user's `prefers-reduced-motion` setting.
    ref.current.scrollIntoView({ behavior: 'smooth', block: 'center' })
  }, [children, rest.intent])

  // Wrapping in a div keeps the call site identical to a plain MessageBar (no ref-forwarding
  // assumptions) and gives us a stable element to call scrollIntoView on. The wrapper has no
  // box-model styling so flex/grid parents still control spacing via their own `gap`.
  return (
    <div ref={ref} className={s.wrap}>
      <MessageBar {...rest}>{children}</MessageBar>
    </div>
  )
}

// Best-effort flattening of a React subtree to its visible text. Good enough to detect "did the
// message change?" without pulling in a heavy dependency or rendering the tree off-screen.
function flattenText(node: ReactNode): string {
  if (node == null || typeof node === 'boolean') return ''
  if (typeof node === 'string' || typeof node === 'number') return String(node)
  if (Array.isArray(node)) return node.map(flattenText).join('')
  if (typeof node === 'object' && 'props' in (node as { props?: { children?: ReactNode } })) {
    return flattenText((node as { props?: { children?: ReactNode } }).props?.children)
  }
  return ''
}
