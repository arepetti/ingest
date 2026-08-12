/**
 * Helpers for working with a schema's UI-only layout tree.
 *
 *  - `walkLayout(schema, opts?)` produces an ordered flat list of render items the consumer
 *    (submission editor, submission view drawer, schema editor preview) just maps over.
 *  - When the caller passes an `isValueVisible` predicate the walker runs bottom-up and
 *    automatically elides sections whose every descendant value is hidden — so we never
 *    render a section heading sitting above no inputs.
 *  - `referencedValueNames` / `unassignedValues` / `validateLayout` are the small utilities
 *    the schema editor uses to keep the tray / inline error banner in sync as the admin edits.
 */
import type { Schema, SchemaLayoutNode, SchemaValue } from '../api/types'
import type { TFunction } from 'i18next'

/** A single item emitted by `walkLayout`. */
export type RenderItem =
  | {
      kind: 'section-start'
      /** 0-based nesting depth: 0 = top-level section. */
      depth: number
      caption: string
      description?: string | null
    }
  | { kind: 'section-end'; depth: number }
  | {
      kind: 'value'
      /** Nesting depth of the closest enclosing section, or 0 when at the root. */
      depth: number
      value: SchemaValue
    }

export interface WalkOptions {
  /**
   * Optional visibility predicate. When provided, the walker elides value items whose name
   * makes the predicate return false, and bubbles that elision up through sections — a
   * section disappears entirely when all of its descendants are hidden, including ancestor
   * sections of an all-hidden subtree.
   * When omitted (the schema editor's preview mode), every value and every section is emitted.
   */
  isValueVisible?: (valueName: string) => boolean

  /**
   * When true (the default), values declared on the schema but not referenced by the layout
   * tree are emitted first under no heading. Pass `false` to skip them — useful when the
   * caller wants to render them separately (e.g. in their own pane).
   */
  includeUnassigned?: boolean
}

/**
 * Walk `schema.layout` into an ordered render plan. Section nodes contribute a section-start
 * item, then their children, then a section-end item; value nodes contribute a single value
 * item resolved against `schema.values`. Values referenced by the layout but missing from
 * `schema.values` are skipped (defensive — the server validates this on save).
 */
export function walkLayout(schema: Schema, opts: WalkOptions = {}): RenderItem[] {
  const { isValueVisible, includeUnassigned = true } = opts
  const byName = new Map<string, SchemaValue>()
  for (const v of schema.values) byName.set(v.name.toLowerCase(), v)

  const out: RenderItem[] = []

  if (includeUnassigned) {
    const assigned = referencedValueNames(schema.layout ?? [])
    for (const v of schema.values) {
      if (assigned.has(v.name.toLowerCase())) continue
      if (isValueVisible && !isValueVisible(v.name)) continue
      out.push({ kind: 'value', depth: 0, value: v })
    }
  }

  walk(schema.layout ?? [], 0)
  return out

  function walk(nodes: SchemaLayoutNode[], depth: number): boolean {
    let anyEmitted = false
    for (const node of nodes) {
      if (node.kind === 'value') {
        const v = byName.get(node.valueName.toLowerCase())
        if (!v) continue
        if (isValueVisible && !isValueVisible(v.name)) continue
        out.push({ kind: 'value', depth, value: v })
        anyEmitted = true
      } else if (node.kind === 'section') {
        // Stage the section header optimistically, then drop it if the recursion turns up
        // nothing. This keeps the bottom-up elision cheap (no rescanning) and preserves the
        // depth bookkeeping for nested cases.
        const startIdx = out.length
        out.push({ kind: 'section-start', depth, caption: node.caption, description: node.description })
        const childrenEmitted = walk(node.items ?? [], depth + 1)
        if (childrenEmitted) {
          out.push({ kind: 'section-end', depth })
          anyEmitted = true
        } else {
          // Roll back the staged header so the parent never sees an empty section.
          out.length = startIdx
        }
      }
    }
    return anyEmitted
  }
}

/** Collect every value name referenced (recursively) by a layout tree. Case-insensitive. */
export function referencedValueNames(layout: SchemaLayoutNode[]): Set<string> {
  const seen = new Set<string>()
  visit(layout)
  return seen

  function visit(nodes: SchemaLayoutNode[]) {
    for (const n of nodes) {
      if (n.kind === 'value') seen.add(n.valueName.toLowerCase())
      else if (n.kind === 'section') visit(n.items ?? [])
    }
  }
}

/** Values declared on the schema but not present anywhere in the layout. Preserves declaration order. */
export function unassignedValues(schema: Schema): SchemaValue[] {
  const refs = referencedValueNames(schema.layout ?? [])
  return schema.values.filter(v => !refs.has(v.name.toLowerCase()))
}

/** A single validation issue surfaced inline in the schema editor before save. */
export interface LayoutValidationIssue {
  message: string
  /** Severity hint for the UI. `error` blocks save; `warning` is informational. */
  severity: 'error' | 'warning'
}

/**
 * Client-side mirror of the server's layout validator. The server is authoritative; this lives
 * here so the editor can show errors as the admin edits, without waiting for a save round-trip.
 */
export function validateLayout(schema: Schema, t?: TFunction): LayoutValidationIssue[] {
  const issues: LayoutValidationIssue[] = []
  const valueNames = new Set(schema.values.map(v => v.name.toLowerCase()))
  const seenRefs = new Set<string>()
  visit(schema.layout ?? [], 0)

  const unassigned = unassignedValues(schema)
  if (unassigned.length > 0) {
    issues.push({
      severity: 'warning',
      message: t
        ? t('schemasSubmissions.layout.unassignedWarning', { count: unassigned.length })
        : `${unassigned.length} value${unassigned.length === 1 ? '' : 's'} not placed in the layout — they will render before the sections.`,
    })
  }
  return issues

  function visit(nodes: SchemaLayoutNode[], depth: number) {
    if (depth > 32) {
      issues.push({ severity: 'error', message: t ? t('schemasSubmissions.layout.tooDeep') : 'Layout is nested too deep (max 32 levels).' })
      return
    }
    for (const n of nodes) {
      if (n.kind === 'value') {
        if (!n.valueName?.trim()) {
          issues.push({ severity: 'error', message: t ? t('schemasSubmissions.layout.missingTarget') : 'A layout value node is missing its target.' })
          continue
        }
        const key = n.valueName.toLowerCase()
        if (!valueNames.has(key)) {
          issues.push({ severity: 'error', message: t ? t('schemasSubmissions.layout.unknownValue', { name: n.valueName }) : `Layout references unknown value "${n.valueName}".` })
          continue
        }
        if (!seenRefs.add(key)) {
          issues.push({ severity: 'error', message: t ? t('schemasSubmissions.layout.duplicateValue', { name: n.valueName }) : `Value "${n.valueName}" appears more than once in the layout.` })
        }
      } else if (n.kind === 'section') {
        if (!n.caption?.trim()) {
          issues.push({ severity: 'error', message: t ? t('schemasSubmissions.layout.missingCaption') : 'A section is missing its caption.' })
        }
        visit(n.items ?? [], depth + 1)
      }
    }
  }
}
