/**
 * Lazy wrapper around the CodeMirror-based {@link ExpressionEditor}.
 *
 * The actual editor (and all of CodeMirror) lives in a separate chunk that is only fetched the
 * first time an expression field renders — editable *or* read-only, since read-only fields still
 * want syntax highlighting. Until the chunk lands we render a plain Fluent `Textarea` so the
 * control is always usable and never causes a layout jump.
 */
import { Suspense, lazy } from 'react'
import { Textarea, makeStyles, tokens } from '@fluentui/react-components'
import type { ExpressionEditorProps } from './ExpressionEditor'

// Module-level so the dynamic import (and its chunk) is created exactly once.
const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

const useStyles = makeStyles({
  // Match the editor's full-width footprint so there's no width jump when CodeMirror swaps in.
  fallback: { width: '100%', fontFamily: tokens.fontFamilyMonospace },
})

export type ExpressionFieldProps = ExpressionEditorProps

function PlainTextarea({ value, onChange, rows, disabled, ariaLabel, placeholder }: ExpressionFieldProps) {
  const s = useStyles()
  return (
    <Textarea
      className={s.fallback}
      rows={rows}
      value={value}
      disabled={disabled}
      placeholder={placeholder}
      aria-label={ariaLabel}
      onChange={(_, d) => onChange(d.value)}
    />
  )
}

export function ExpressionField(props: ExpressionFieldProps) {
  return (
    <Suspense fallback={<PlainTextarea {...props} />}>
      <LazyExpressionEditor {...props} />
    </Suspense>
  )
}
