/**
 * Lightweight, CodeMirror-free scan that flags identifiers in an NCalc expression that resolve to
 * neither a known function nor a known variable. Used by the expression editor's linter to draw
 * "unknown function/value" squiggles. Kept dependency-free so it's cheap to unit-test.
 *
 * The scan deliberately skips string literals (`'…'` / `"…"`), date literals (`#…#`), and bracketed
 * bound-key forms (`[name.minimum]`) — those are always treated as valid so they never mis-flag.
 */
import { KNOWN_FUNCTION_NAMES } from './expressionFunctions'

export interface IdentifierProblem {
  from: number
  to: number
  message: string
}

// Words that are operators/literals rather than value references — never flagged as variables.
const NON_VARIABLE_WORDS: ReadonlySet<string> = new Set([
  'and', 'or', 'not', 'in', 'like', 'true', 'false', 'null', 'if',
])

const IDENT_START = /[A-Za-z_]/
const IDENT_PART = /[A-Za-z0-9_]/

export function findUnknownIdentifiers(text: string, knownVarsLower: ReadonlySet<string>): IdentifierProblem[] {
  const problems: IdentifierProblem[] = []
  const n = text.length
  let i = 0
  while (i < n) {
    const c = text[i]
    // Skip string literals (with backslash escapes).
    if (c === "'" || c === '"') {
      const quote = c
      i++
      while (i < n) {
        if (text[i] === '\\') { i += 2; continue }
        if (text[i] === quote) { i++; break }
        i++
      }
      continue
    }
    // Skip #date# literals and [bound.key] references.
    if (c === '#') { i++; while (i < n && text[i] !== '#') i++; if (i < n) i++; continue }
    if (c === '[') { i++; while (i < n && text[i] !== ']') i++; if (i < n) i++; continue }
    // Identifier.
    if (IDENT_START.test(c)) {
      const start = i
      i++
      while (i < n && IDENT_PART.test(text[i])) i++
      const word = text.slice(start, i)
      const lower = word.toLowerCase()
      // Peek past whitespace: a following "(" means this is a function call.
      let j = i
      while (j < n && /\s/.test(text[j])) j++
      if (text[j] === '(') {
        if (!KNOWN_FUNCTION_NAMES.has(lower)) {
          problems.push({ from: start, to: i, message: `Unknown function '${word}'.` })
        }
      } else if (!NON_VARIABLE_WORDS.has(lower) && !knownVarsLower.has(lower)) {
        problems.push({ from: start, to: i, message: `Unknown value '${word}'.` })
      }
      continue
    }
    i++
  }
  return problems
}
