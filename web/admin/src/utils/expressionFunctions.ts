/**
 * Metadata for the built-in functions available in validation / calculated-value expressions.
 * Mirrors the server-side `BuiltIns` table in `NCalcExpressionEvaluator` (plus the `if` operator
 * the parser treats specially) and drives the expression editor's autocomplete suggestions.
 *
 * Context-specific helpers (`latest()`, `previous()`, `serviceName()`, …) are intentionally left
 * out: they're only valid in some rule positions and are rejected in calculated expressions, so
 * suggesting them everywhere would mislead.
 */
export interface ExprFunctionMeta {
  /** Function name as written in the expression (case-insensitive at runtime). */
  name: string
  /** Human-readable call signature shown in the suggestion detail. */
  signature: string
  /** One-line description. */
  description: string
}

export const EXPRESSION_FUNCTIONS: readonly ExprFunctionMeta[] = [
  // App built-ins (mirror NCalcExpressionEvaluator.BuiltIns + the `if` operator).
  { name: 'if', signature: 'if(condition, then, else)', description: 'Returns "then" when the condition is true, otherwise "else".' },
  { name: 'average', signature: 'average(a, b, …)', description: 'Mean of the numeric arguments. Booleans count as 1/0; nulls are ignored.' },
  { name: 'higher_than', signature: 'higher_than(value, reference, percentage)', description: 'True when value exceeds reference by more than percentage% (e.g. 50 = 50%).' },
  { name: 'coalesce', signature: 'coalesce(a, b, …)', description: 'First argument that is not null.' },
  { name: 'isNull', signature: 'isNull(value)', description: 'True when the value is null.' },
  { name: 'len', signature: 'len(text)', description: 'Length of a string.' },
  { name: 'now', signature: 'now()', description: 'Current UTC date and time.' },
  { name: 'today', signature: 'today()', description: 'Current UTC date at midnight.' },
  { name: 'year', signature: 'year(date)', description: 'Year component of a date.' },
  { name: 'month', signature: 'month(date)', description: 'Month component of a date (1-12).' },
  { name: 'dayOfMonth', signature: 'dayOfMonth(date)', description: 'Day of the month (1-31).' },
  { name: 'dayOfWeek', signature: 'dayOfWeek(date)', description: 'Day of the week (0 = Sunday).' },
  { name: 'dayOfYear', signature: 'dayOfYear(date)', description: 'Day of the year (1-366).' },
  { name: 'weekOfYear', signature: 'weekOfYear(date)', description: 'ISO-8601 week number.' },
  { name: 'hour', signature: 'hour(date)', description: 'Hour component (0-23).' },
  { name: 'minute', signature: 'minute(date)', description: 'Minute component (0-59).' },
  { name: 'second', signature: 'second(date)', description: 'Second component (0-59).' },
  // NCalc native functions (resolved by the engine when not overridden). NCalc matches function
  // names case-insensitively, so they're offered lower-cased to match the rest of the dialect.
  { name: 'abs', signature: 'abs(number)', description: 'Absolute value.' },
  { name: 'ceiling', signature: 'ceiling(number)', description: 'Smallest integer greater than or equal to the number.' },
  { name: 'floor', signature: 'floor(number)', description: 'Largest integer less than or equal to the number.' },
  { name: 'round', signature: 'round(number, decimals)', description: 'Round to the given number of decimal places.' },
  { name: 'truncate', signature: 'truncate(number)', description: 'Integer part of the number (drops the fraction).' },
  { name: 'sign', signature: 'sign(number)', description: 'Sign of the number: -1, 0 or 1.' },
  { name: 'sqrt', signature: 'sqrt(number)', description: 'Square root.' },
  { name: 'pow', signature: 'pow(base, exponent)', description: 'Base raised to the exponent.' },
  { name: 'exp', signature: 'exp(number)', description: 'e raised to the given power.' },
  { name: 'log', signature: 'log(number, base)', description: 'Logarithm of the number in the given base.' },
  { name: 'log10', signature: 'log10(number)', description: 'Base-10 logarithm.' },
  { name: 'max', signature: 'max(a, b)', description: 'Larger of the two numbers.' },
  { name: 'min', signature: 'min(a, b)', description: 'Smaller of the two numbers.' },
  { name: 'ieeeRemainder', signature: 'ieeeRemainder(x, y)', description: 'IEEE 754 remainder of x divided by y.' },
  { name: 'sin', signature: 'sin(number)', description: 'Sine (radians).' },
  { name: 'cos', signature: 'cos(number)', description: 'Cosine (radians).' },
  { name: 'tan', signature: 'tan(number)', description: 'Tangent (radians).' },
  { name: 'asin', signature: 'asin(number)', description: 'Arc sine (radians).' },
  { name: 'acos', signature: 'acos(number)', description: 'Arc cosine (radians).' },
  { name: 'atan', signature: 'atan(number)', description: 'Arc tangent (radians).' },
]

/** Lower-cased function names offered by autocomplete, for quick "is this a known function" checks. */
export const EXPRESSION_FUNCTION_NAMES: ReadonlySet<string> =
  new Set(EXPRESSION_FUNCTIONS.map(f => f.name.toLowerCase()))

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
