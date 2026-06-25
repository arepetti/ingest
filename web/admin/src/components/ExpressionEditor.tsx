/**
 * CodeMirror 6 editor for NCalc expressions — syntax highlighting + autocomplete.
 *
 * This module pulls in all of CodeMirror, so it is **never imported statically**: it is the
 * default export loaded via `React.lazy(() => import('./ExpressionEditor'))` from
 * `ExpressionField`, which keeps every `@codemirror/*` package out of the main bundle and only
 * downloads it the first time an expression field actually renders.
 *
 * Highlighting and the autocomplete popup are themed with Fluent's CSS custom properties
 * (`var(--colorXxx)`) rather than hard-coded colours, so the editor follows the app's light/dark
 * theme automatically.
 */
import { useEffect, useRef } from 'react'
import { makeStyles, tokens } from '@fluentui/react-components'
import { EditorState } from '@codemirror/state'
import { EditorView, keymap, placeholder as cmPlaceholder } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands'
import { HighlightStyle, StreamLanguage, syntaxHighlighting } from '@codemirror/language'
import {
  autocompletion, completionKeymap, closeBrackets, closeBracketsKeymap,
  type Completion, type CompletionContext, type CompletionResult,
} from '@codemirror/autocomplete'
import { linter, type Diagnostic } from '@codemirror/lint'
import { tags as t } from '@lezer/highlight'
import { EXPRESSION_FUNCTIONS, EXPRESSION_FUNCTION_NAMES } from '../utils/expressionFunctions'
import { validateExpression } from '../utils/expression'
import { findUnknownIdentifiers } from '../utils/expressionLint'

export interface ExpressionEditorProps {
  value: string
  onChange: (next: string) => void
  /** Value names / context variables offered as autocomplete suggestions. */
  identifiers?: string[]
  /** Visible height in text rows (min-height; the editor still grows with content). */
  rows?: number
  /** Read-only mode: still syntax-highlighted, but not editable and without autocomplete. */
  disabled?: boolean
  /** Show error squiggles (server syntax check + unknown variable/function detection). */
  lint?: boolean
  ariaLabel?: string
  placeholder?: string
}

const useStyles = makeStyles({
  root: {
    borderTopStyle: 'solid', borderRightStyle: 'solid', borderBottomStyle: 'solid', borderLeftStyle: 'solid',
    borderTopWidth: '1px', borderRightWidth: '1px', borderBottomWidth: '1px', borderLeftWidth: '1px',
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase300,
    overflow: 'hidden',
    ':focus-within': {
      borderTopColor: tokens.colorCompoundBrandStroke,
      borderRightColor: tokens.colorCompoundBrandStroke,
      borderBottomColor: tokens.colorCompoundBrandStroke,
      borderLeftColor: tokens.colorCompoundBrandStroke,
    },
  },
})

// NCalc tokenizer. Returns CodeMirror-5 legacy style names, which StreamLanguage maps to the
// standard highlight tags our HighlightStyle below is keyed on (e.g. 'builtin' → function calls).
const KEYWORDS = new Set(['and', 'or', 'not', 'in', 'like', 'true', 'false', 'null', 'if'])
const ncalcLanguage = StreamLanguage.define<unknown>({
  token(stream) {
    if (stream.eatSpace()) return null
    // Quoted strings (single or double, with backslash escapes).
    if (stream.match(/^'([^'\\]|\\.)*'/) || stream.match(/^"([^"\\]|\\.)*"/)) return 'string'
    // Bracketed variable form the server uses for dotted keys, e.g. [revenue.minimum].
    if (stream.match(/^\[[^\]]*\]/)) return 'variable'
    if (stream.match(/^\d+(\.\d+)?/)) return 'number'
    const id = stream.match(/^[A-Za-z_][A-Za-z0-9_]*/) as RegExpMatchArray | null
    if (id) {
      const word = id[0].toLowerCase()
      if (KEYWORDS.has(word)) return 'keyword'
      // A call (name followed by "(") or a known built-in reads as a function.
      if (/^\s*\(/.test(stream.string.slice(stream.pos)) || EXPRESSION_FUNCTION_NAMES.has(word)) return 'builtin'
      return 'variable'
    }
    if (stream.match(/^(<=|>=|==|!=|<>|&&|\|\||[-+*/%<>=!&|^~])/)) return 'operator'
    stream.next()
    return null
  },
})

const highlightStyle = HighlightStyle.define([
  { tag: t.keyword, color: 'var(--colorPaletteBerryForeground2)', fontWeight: '600' },
  { tag: t.number, color: 'var(--colorPaletteGreenForeground2)' },
  { tag: t.string, color: 'var(--colorPaletteRedForeground2)' },
  { tag: t.operator, color: 'var(--colorNeutralForeground3)' },
  { tag: t.variableName, color: 'var(--colorNeutralForeground1)' },
  // Legacy 'builtin' → t.standard(t.variableName): our function calls.
  { tag: t.standard(t.variableName), color: 'var(--colorPaletteBlueForeground2)', fontWeight: '600' },
  { tag: t.bracket, color: 'var(--colorPaletteBlueForeground2)' },
])

// Language keywords/literals offered alongside functions and variables. The `type` drives the
// little icon CodeMirror renders to the left of each suggestion (keyword / function / variable),
// so the three kinds are visually distinguishable in the popup.
const KEYWORD_COMPLETIONS: Completion[] = [
  { label: 'true', type: 'keyword', detail: 'boolean literal' },
  { label: 'false', type: 'keyword', detail: 'boolean literal' },
  { label: 'null', type: 'keyword', detail: 'null literal' },
  { label: 'and', type: 'keyword', detail: 'logical and' },
  { label: 'or', type: 'keyword', detail: 'logical or' },
  { label: 'not', type: 'keyword', detail: 'logical not' },
  { label: 'in', type: 'keyword', detail: 'membership test' },
  { label: 'like', type: 'keyword', detail: 'SQL-style pattern match' },
]

function buildCompletionSource(getIdentifiers: () => string[]) {
  const fnOptions: Completion[] = EXPRESSION_FUNCTIONS.map(f => ({
    label: f.name,
    type: 'function',
    detail: f.signature,
    info: f.description,
    // Insert the opening paren so the caret lands inside the call.
    apply: f.name + '(',
  }))
  return (ctx: CompletionContext): CompletionResult | null => {
    const word = ctx.matchBefore(/[A-Za-z_]\w*/)
    if (!word || (word.from === word.to && !ctx.explicit)) return null
    const identifierOptions: Completion[] = getIdentifiers().map(name => ({ label: name, type: 'variable' }))
    return {
      from: word.from,
      options: [...identifierOptions, ...fnOptions, ...KEYWORD_COMPLETIONS],
      validFor: /^[A-Za-z_]\w*$/,
    }
  }
}

// Server-backed linter: re-runs (debounced) on edits and underlines problems with a red squiggle +
// hover message. First the server syntax check; if that passes, a client-side pass flags unknown
// variables/functions against the field's identifiers. Shares `validateExpression`'s cache with the
// inline status indicator, so the two never cost more than one network round-trip per change.
function buildLinter(getIdentifiers: () => string[]) {
  return linter(async (view): Promise<Diagnostic[]> => {
    const text = view.state.doc.toString()
    if (!text.trim()) return []
    const res = await validateExpression(text)
    if (!res.ok) {
      const len = text.length
      let from = typeof res.position === 'number' ? res.position : 0
      from = Math.max(0, Math.min(from, len))
      const to = len
      // Guarantee a visible range even when the parser points at end-of-input.
      if (from >= to && to > 0) from = to - 1
      return [{ from, to, severity: 'error', message: res.error }]
    }
    const knownVars = new Set(getIdentifiers().map(name => name.toLowerCase()))
    return findUnknownIdentifiers(text, knownVars)
      .map(p => ({ from: p.from, to: p.to, severity: 'error' as const, message: p.message }))
  }, { delay: 350 })
}

function editorTheme(rows: number) {
  return EditorView.theme({
    '&': { backgroundColor: 'transparent', fontSize: 'inherit' },
    '&.cm-focused': { outline: 'none' },
    '.cm-scroller': { fontFamily: 'inherit', lineHeight: '1.5' },
    '.cm-content': {
      fontFamily: 'inherit',
      padding: '6px 8px',
      minHeight: `${Math.max(1, rows) * 1.5}em`,
      caretColor: 'var(--colorNeutralForeground1)',
    },
    '.cm-placeholder': { color: 'var(--colorNeutralForeground4)' },
    '.cm-cursor': { borderLeftColor: 'var(--colorNeutralForeground1)' },
    '.cm-selectionBackground, &.cm-focused .cm-selectionBackground': {
      backgroundColor: 'var(--colorNeutralBackground1Selected)',
    },
    // Autocomplete popup, themed to match Fluent menus.
    '.cm-tooltip.cm-tooltip-autocomplete': {
      border: '1px solid var(--colorNeutralStroke1)',
      borderRadius: '4px',
      backgroundColor: 'var(--colorNeutralBackground1)',
      boxShadow: 'var(--shadow16)',
    },
    '.cm-tooltip-autocomplete > ul': { fontFamily: 'inherit', maxHeight: '16em' },
    '.cm-tooltip-autocomplete > ul > li': { padding: '3px 8px', color: 'var(--colorNeutralForeground1)' },
    '.cm-tooltip-autocomplete > ul > li[aria-selected]': {
      backgroundColor: 'var(--colorNeutralBackground1Selected)',
      color: 'var(--colorNeutralForeground1)',
    },
    '.cm-completionDetail': { color: 'var(--colorNeutralForeground3)', fontStyle: 'italic', marginLeft: '8px' },
    '.cm-completionInfo': {
      border: '1px solid var(--colorNeutralStroke1)',
      borderRadius: '4px',
      backgroundColor: 'var(--colorNeutralBackground1)',
      color: 'var(--colorNeutralForeground2)',
      padding: '6px 8px',
    },
    // The kind icon (ƒ for functions, etc.) colour-coded so keyword / function / variable read apart.
    '.cm-completionIcon': { opacity: '1', paddingRight: '0.6em', boxSizing: 'content-box' },
    '.cm-completionIcon-function': { color: 'var(--colorPaletteBlueForeground2)' },
    '.cm-completionIcon-variable': { color: 'var(--colorPaletteGreenForeground2)' },
    '.cm-completionIcon-keyword': { color: 'var(--colorPaletteBerryForeground2)' },
    // Lint squiggle hover tooltip, themed to match Fluent.
    '.cm-tooltip.cm-tooltip-lint': {
      border: '1px solid var(--colorNeutralStroke1)',
      borderRadius: '4px',
      backgroundColor: 'var(--colorNeutralBackground1)',
      boxShadow: 'var(--shadow16)',
    },
    '.cm-diagnostic': { fontFamily: 'inherit', color: 'var(--colorNeutralForeground1)' },
    '.cm-diagnostic-error': { borderLeftColor: 'var(--colorPaletteRedBorderActive)' },
  })
}

export default function ExpressionEditor({ value, onChange, identifiers, rows = 3, disabled, lint, ariaLabel, placeholder }: ExpressionEditorProps) {
  const s = useStyles()
  const hostRef = useRef<HTMLDivElement | null>(null)
  const viewRef = useRef<EditorView | null>(null)
  // Keep the latest callbacks/data in refs so the view is built once and never torn down on
  // every keystroke or sibling-name change. Refreshed in an effect (not during render).
  const onChangeRef = useRef(onChange)
  const identifiersRef = useRef(identifiers ?? [])
  useEffect(() => {
    onChangeRef.current = onChange
    identifiersRef.current = identifiers ?? []
  })

  useEffect(() => {
    if (!hostRef.current) return
    // Always-on extensions: highlighting, wrapping, theme, accessibility.
    const extensions = [
      ncalcLanguage,
      syntaxHighlighting(highlightStyle),
      EditorView.lineWrapping,
      editorTheme(rows),
      EditorView.contentAttributes.of({
        'aria-label': ariaLabel ?? 'Expression editor',
        role: 'textbox',
        'aria-multiline': 'true',
        'aria-readonly': disabled ? 'true' : 'false',
      }),
    ]
    if (disabled) {
      // Read-only: highlighted but not editable, and no autocomplete/history machinery.
      extensions.push(EditorState.readOnly.of(true), EditorView.editable.of(false))
    } else {
      extensions.push(
        history(),
        closeBrackets(),
        keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap, ...completionKeymap]),
        autocompletion({ override: [buildCompletionSource(() => identifiersRef.current)] }),
        ...(lint ? [buildLinter(() => identifiersRef.current)] : []),
        EditorView.updateListener.of(u => {
          if (u.docChanged) onChangeRef.current(u.state.doc.toString())
        }),
        ...(placeholder ? [cmPlaceholder(placeholder)] : []),
      )
    }
    const view = new EditorView({
      parent: hostRef.current,
      state: EditorState.create({ doc: value, extensions }),
    })
    viewRef.current = view
    return () => { view.destroy(); viewRef.current = null }
    // Built once on mount; live data flows through refs and the value-sync effect below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Push external value changes (e.g. switching the edited value in the drawer) into the doc,
  // but only when they actually differ — otherwise we'd fight the user's own typing.
  useEffect(() => {
    const view = viewRef.current
    if (!view) return
    const current = view.state.doc.toString()
    if (current !== value) {
      view.dispatch({ changes: { from: 0, to: current.length, insert: value } })
    }
  }, [value])

  return <div ref={hostRef} className={s.root} />
}
