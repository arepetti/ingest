/**
 * Shared rule-evaluation logic for the submission form and the schema editor preview.
 *
 * Both surfaces render a form from a `Schema`, collect a value per row, and evaluate the
 * schema's expression rules client-side so the user gets live feedback (hide/grey/warning,
 * and — in the preview — value/schema validation pass/fail). The actual translation +
 * evaluation lives in `expression.ts`; this module owns the submission-shaped glue: the row
 * model, the variable context the server mirrors, the gating verdict, and a hook that
 * prefetches every rule on a schema and re-renders once translations land.
 *
 * The server remains authoritative — every helper here falls back to a permissive verdict
 * when a rule can't be translated/evaluated, so a buggy or not-yet-translated rule never makes
 * the UI lie in the rejecting direction.
 */
import { useEffect, useMemo, useState } from 'react'
import type { Schema, SchemaValue } from '../api/types'
import { ExpressionError, isTruthy, prefetchExpressions, tryEvaluateExpression } from './expression'

/** One editable row in a submission/preview form: the value definition plus what the user typed. */
export interface ValueRow {
  /** value name (unique within the schema) */
  name: string
  def: SchemaValue
  /** Raw value as the user typed it; serialized on save. */
  value: unknown
  note: string
}

/** Outcome of client-side EnabledIf/VisibleIf/Warning evaluation for a single row. */
export interface RowState {
  name: string
  /** True when VisibleIf is set and evaluates falsy — the row is not rendered at all. */
  hidden: boolean
  /** True when EnabledIf is set and evaluates falsy — the row is rendered read-only. */
  disabled: boolean
  /** True when this row is effectively dropped from the payload (hidden OR disabled). */
  discarded: boolean
  /** Optional live warning text from the per-value Warning rule. */
  warning: string | null
}

/** Verdict for a validation rule (value-level or schema-level), mirroring the server's contract. */
export interface RuleVerdict {
  ok: boolean
  /** Set when the rule rejected with a custom (non-empty string) message. */
  message?: string
}

/** A value counts as "filled" when it isn't null/undefined/blank. false/0 are valid filled values. */
export function isFilled(v: unknown): boolean {
  if (v === null || v === undefined) return false
  if (typeof v === 'string') return v.trim() !== ''
  return true
}

/**
 * Build the variable context client-side rules evaluate against. Mirrors the server's
 * `BuildRuleContext`: every declared value is exposed by name (null when the row is empty),
 * and each numeric value with bounds also contributes `<name>.minimum` / `<name>.maximum`
 * keys. The dotted keys are unreachable as plain identifiers in NCalc — the server registers
 * them under the bracket form `[name.minimum]`, which `helpers.var` looks up case-insensitively.
 */
export function buildRuleVariables(rows: ValueRow[]): Record<string, unknown> {
  const ctx: Record<string, unknown> = {}
  for (const r of rows) {
    ctx[r.name] = isFilled(r.value) ? r.value : null
    const isNumeric = r.def.type === 'Integer' || r.def.type === 'Number'
    if (isNumeric) {
      if (r.def.min !== null && r.def.min !== undefined) ctx[`${r.name}.minimum`] = r.def.min
      if (r.def.max !== null && r.def.max !== undefined) ctx[`${r.name}.maximum`] = r.def.max
    }
  }
  return ctx
}

/**
 * Evaluate the three display/warning rule fields against the live submission context. Returns a
 * `RowState` describing whether the row should be hidden, disabled, or carrying a warning.
 * When a rule fails to parse/evaluate we fall back to "show + no warning" — the server remains
 * authoritative so a buggy rule never makes data silently disappear from the editor.
 */
export function evaluateGating(row: ValueRow, variables: Record<string, unknown>): RowState {
  // The unified `variables` bag already carries every value by its name (including this row's
  // own current input) plus the `<name>.minimum` / `<name>.maximum` bound keys. No alias
  // injection needed — rules reference values explicitly. tryEvaluateExpression returns
  // undefined when translation hasn't landed yet; we treat that the same as "no rule" so the
  // UI stays permissive until the verdict is in.
  const visEval = safeEval(row.def.visibleIf, variables)
  const enaEval = safeEval(row.def.enabledIf, variables)
  const warnEval = safeEval(row.def.warning, variables)

  const hidden = visEval !== undefined && !isTruthy(visEval)
  const disabled = enaEval !== undefined && !isTruthy(enaEval)

  let warning: string | null = null
  if (warnEval !== undefined) {
    if (typeof warnEval === 'string' && warnEval.trim().length > 0) warning = warnEval
    else if (warnEval === true) warning = 'Warning rule triggered.'
  }

  return {
    name: row.name,
    hidden,
    disabled,
    discarded: hidden || disabled,
    warning,
  }
}

/**
 * Interpret a validation rule's result the way the server does (see admin-user-guide/validation.md):
 * `true` / `null` / `''` (and "not translated yet" → undefined) accept; `false` rejects with a
 * generic message; a non-empty string rejects and is surfaced verbatim. Any other truthy value
 * accepts, any other falsy value rejects — defensive only; rules normally return bool or string.
 */
export function interpretRuleResult(result: unknown): RuleVerdict {
  if (result === undefined || result === null) return { ok: true }
  if (typeof result === 'string') {
    return result.trim().length > 0 ? { ok: false, message: result } : { ok: true }
  }
  if (result === true) return { ok: true }
  if (result === false) return { ok: false }
  return isTruthy(result) ? { ok: true } : { ok: false }
}

/**
 * Best-effort synchronous evaluation that swallows translation/runtime errors into `undefined`
 * (= "no decision yet"), so callers can treat a not-yet-translated or broken rule as "no rule".
 */
export function safeEval(expr: string | null | undefined, variables: Record<string, unknown>): unknown | undefined {
  if (!expr || !expr.trim()) return undefined
  try {
    return tryEvaluateExpression(expr, variables)
  } catch (e) {
    if (e instanceof ExpressionError) return undefined
    return undefined
  }
}

/**
 * Prefetch every rule on `schema` (display, warning, value-validation, and schema-level
 * validations), recompute the per-row gating against the current `rows`, and re-render once
 * translations settle. Returns the gating verdicts plus the shared variable context so callers
 * can evaluate additional rules (e.g. the preview's validation panel) against the same bag.
 */
export function useSampleRules(schema: Schema | undefined, rows: ValueRow[]) {
  // Bumped after the translator has fetched all rule scripts for the current schema. Used purely
  // as a re-render trigger so the gating memo re-runs once concrete results are available.
  const [rulesReady, setRulesReady] = useState(0)

  useEffect(() => {
    if (!schema) return
    const exprs: (string | null | undefined)[] = []
    for (const v of schema.values) {
      exprs.push(v.enabledIf, v.visibleIf, v.warning, v.valueValidation)
    }
    for (const rule of schema.submissionValidations ?? []) exprs.push(rule)
    let cancelled = false
    prefetchExpressions(exprs).then(() => {
      if (!cancelled) setRulesReady(t => t + 1)
    })
    return () => { cancelled = true }
  }, [schema])

  const ruleVariables = useMemo(() => buildRuleVariables(rows), [rows])

  const rowStates = useMemo(() => {
    // `rulesReady` is read here so the memo invalidates once new translations land in the
    // cache. The cache lookup itself happens inside evaluateGating.
    void rulesReady
    return rows.map(r => evaluateGating(r, ruleVariables))
  }, [rows, ruleVariables, rulesReady])

  return { rowStates, ruleVariables, rulesReady }
}
