/**
 * Metadata for the built-in functions available in validation / calculated-value expressions.
 * Mirrors the server-side `BuiltIns` table in `NCalcExpressionEvaluator` (plus the `if` operator
 * the parser treats specially) and drives the expression editor's autocomplete suggestions.
 *
 * Context-specific helpers (`latest()`, `previous()`, `serviceName()`, …) are intentionally left
 * out: they're only valid in some rule positions and are rejected in calculated expressions, so
 * suggesting them everywhere would mislead.
 */
import type { TFunction } from 'i18next'
import i18n from '../i18n'

export interface ExprFunctionMeta {
  /** Function name as written in the expression (case-insensitive at runtime). */
  name: string
  /** Human-readable call signature shown in the suggestion detail. */
  signature: string
  /** One-line description. */
  description: string
}

const EXPRESSION_FUNCTION_DEFINITIONS: ReadonlyArray<Omit<ExprFunctionMeta, 'description'>> = [
  // App built-ins (mirror NCalcExpressionEvaluator.BuiltIns + the `if` operator).
  { name: 'if', signature: 'if(condition, then, else)' },
  { name: 'average', signature: 'average(a, b, …)' },
  { name: 'higher_than', signature: 'higher_than(value, reference, percentage)' },
  { name: 'coalesce', signature: 'coalesce(a, b, …)' },
  { name: 'isNull', signature: 'isNull(value)' },
  { name: 'len', signature: 'len(text)' },
  { name: 'now', signature: 'now()' },
  { name: 'today', signature: 'today()' },
  { name: 'year', signature: 'year(date)' },
  { name: 'month', signature: 'month(date)' },
  { name: 'dayOfMonth', signature: 'dayOfMonth(date)' },
  { name: 'dayOfWeek', signature: 'dayOfWeek(date)' },
  { name: 'dayOfYear', signature: 'dayOfYear(date)' },
  { name: 'weekOfYear', signature: 'weekOfYear(date)' },
  { name: 'hour', signature: 'hour(date)' },
  { name: 'minute', signature: 'minute(date)' },
  { name: 'second', signature: 'second(date)' },
  // NCalc native functions (resolved by the engine when not overridden). NCalc matches function
  // names case-insensitively, so they're offered lower-cased to match the rest of the dialect.
  { name: 'abs', signature: 'abs(number)' },
  { name: 'ceiling', signature: 'ceiling(number)' },
  { name: 'floor', signature: 'floor(number)' },
  { name: 'round', signature: 'round(number, decimals)' },
  { name: 'truncate', signature: 'truncate(number)' },
  { name: 'sign', signature: 'sign(number)' },
  { name: 'sqrt', signature: 'sqrt(number)' },
  { name: 'pow', signature: 'pow(base, exponent)' },
  { name: 'exp', signature: 'exp(number)' },
  { name: 'log', signature: 'log(number, base)' },
  { name: 'log10', signature: 'log10(number)' },
  { name: 'max', signature: 'max(a, b)' },
  { name: 'min', signature: 'min(a, b)' },
  { name: 'ieeeRemainder', signature: 'ieeeRemainder(x, y)' },
  { name: 'sin', signature: 'sin(number)' },
  { name: 'cos', signature: 'cos(number)' },
  { name: 'tan', signature: 'tan(number)' },
  { name: 'asin', signature: 'asin(number)' },
  { name: 'acos', signature: 'acos(number)' },
  { name: 'atan', signature: 'atan(number)' },
]

export function getExpressionFunctions(t: TFunction = i18n.t): readonly ExprFunctionMeta[] {
  return EXPRESSION_FUNCTION_DEFINITIONS.map(fn => ({
    ...fn,
    description: t(`shell.expression.functions.${fn.name}.description`),
  }))
}

/** @deprecated Prefer {@link getExpressionFunctions}; retained for cross-slice compatibility. */
export const EXPRESSION_FUNCTIONS = new Proxy([] as ExprFunctionMeta[], {
  get: (_, property) => Reflect.get(getExpressionFunctions(), property),
})

/** Lower-cased function names offered by autocomplete, for quick "is this a known function" checks. */
export const EXPRESSION_FUNCTION_NAMES: ReadonlySet<string> =
  new Set(EXPRESSION_FUNCTION_DEFINITIONS.map(f => f.name.toLowerCase()))

/**
 * Every function name the server's evaluator accepts (lower-cased) — used by the editor's linter
 * to avoid false-positive "unknown function" squiggles. Everything in {@link EXPRESSION_FUNCTIONS}
 * plus the context-only helpers (`latest`/`previous`/`serviceName`, valid in validation rules) and
 * the operators that can also be written in call form.
 */
export const KNOWN_FUNCTION_NAMES: ReadonlySet<string> = new Set([
  ...EXPRESSION_FUNCTION_NAMES,
  // Context helpers registered for validation rules.
  'latest', 'previous', 'servicename',
  // Operators that can be written in call form.
  'in', 'not',
])
