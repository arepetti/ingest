/**
 * Dependency graph between a schema's own values — powers the schema editor's "Dependencies"
 * diagram (`SchemaDependencyGraphDialog`). Unlike the client-side approximations elsewhere in
 * this app (e.g. `sampleRules.ts`'s identifier extraction for calculated-value ordering), this is
 * a *real* dependency walk: every rule expression is sent to `POST /api/expressions/dependencies`,
 * which parses it with the same NCalc AST the server uses to enforce these rules
 * (`NCalcTranslator`/`ExpressionReferences`) and reports back exactly which identifiers it
 * references — so what you see in the diagram matches what the server actually does with the rule,
 * not a guess. The graph-assembly logic here (`collectDependencyRules` / `assembleDependencyGraph`)
 * is pure and synchronous — the network round trip is isolated to `buildDependencyGraph`.
 */
import { api } from '../api/client'
import type { ExpressionDependencyResult, Schema, SchemaValue } from '../api/types'
import { walkLayout, type RenderItem } from './layout'

export type DependencyEdgeKind = 'calculated' | 'visibleIf' | 'enabledIf' | 'validation' | 'warning' | 'submissionValidation'

/** One value, as shown in the dependency diagram. */
export interface DependencyNode {
  name: string
  label: string
  /** Derived (kind === 'Calculated') values are drawn slightly differently — same shape, no tag. */
  calculated: boolean
}

/**
 * One drawn connector. `from` is the value being referenced (the dependency), `to` is the value
 * whose rule references it — so a directed edge reads "`to` depends on `from`". Schema-level
 * `submissionValidations` reference several values with no single "owner", so those edges are
 * undirected (`directed: false`) chains between the values the rule mentions instead.
 */
export interface DependencyEdge {
  kind: DependencyEdgeKind
  from: string
  to: string
  /** Full source expression driving this edge — shown verbatim in the connector's tooltip. */
  expression: string
  directed: boolean
}

export interface DependencyGraph {
  nodes: DependencyNode[]
  edges: DependencyEdge[]
}

/**
 * One rule expression to resolve against the schema's values. `owner` is the value the rule is
 * declared on (blank for schema-level `submissionValidations`, which have no single owner).
 * Exported so `assembleDependencyGraph` can be unit-tested without a network round trip.
 */
export interface DependencyRuleRef {
  owner: string
  kind: DependencyEdgeKind
  expression: string
}

/** Every usable value on `schema` (blank/duplicate names dropped), in layout reading order. */
function usableSchemaValues(schema: Schema): SchemaValue[] {
  const ordered = walkLayout(schema)
    .filter((item): item is RenderItem & { kind: 'value' } => item.kind === 'value')
    .map(item => item.value)

  const seenNames = new Set<string>()
  const defs: SchemaValue[] = []
  for (const v of ordered) {
    const name = (v.name ?? '').trim()
    if (!name || seenNames.has(name.toLowerCase())) continue
    seenNames.add(name.toLowerCase())
    defs.push(v)
  }
  return defs
}

/**
 * Build the node list and the flat set of rule expressions to resolve for `schema` — everything
 * `buildDependencyGraph` needs before it can call the server. Pure and synchronous.
 */
export function collectDependencyRules(schema: Schema): { nodes: DependencyNode[]; refs: DependencyRuleRef[] } {
  const defs = usableSchemaValues(schema)
  const nodes: DependencyNode[] = defs.map(v => ({
    name: v.name.trim(),
    label: v.label?.trim() || v.name.trim(),
    calculated: v.kind === 'Calculated',
  }))

  const refs: DependencyRuleRef[] = []
  function add(owner: string, expr: string | null | undefined, kind: DependencyEdgeKind) {
    if (expr && expr.trim()) refs.push({ owner, kind, expression: expr.trim() })
  }
  for (const v of defs) {
    const name = v.name.trim()
    if (v.kind === 'Calculated') add(name, v.expression, 'calculated')
    add(name, v.visibleIf, 'visibleIf')
    add(name, v.enabledIf, 'enabledIf')
    add(name, v.valueValidation, 'validation')
    add(name, v.warning, 'warning')
  }
  for (const rule of schema.submissionValidations ?? []) add('', rule, 'submissionValidation')

  return { nodes, refs }
}

/** Strip a trailing `.minimum`/`.maximum` bound-key suffix, matching the server's bound-key handling, so a `[peak.minimum]` reference resolves to the `peak` node instead of being dropped. */
function baseIdentifierName(id: string): string {
  const m = /^(.*)\.(?:minimum|maximum)$/i.exec(id)
  return m ? m[1] : id
}

function dedupeCaseInsensitive(names: string[]): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const n of names) {
    const lower = n.toLowerCase()
    if (seen.has(lower)) continue
    seen.add(lower)
    out.push(n)
  }
  return out
}

/**
 * Assemble the final graph from `nodes`/`refs` (see `collectDependencyRules`) plus the server's
 * parsed identifiers for each ref, in the same order (`identifierLists[i]` answers `refs[i]`).
 * Pure and synchronous — the one function that matters for "is this a real dependency walk",
 * fully unit-testable without a network round trip.
 */
export function assembleDependencyGraph(
  nodes: DependencyNode[],
  refs: DependencyRuleRef[],
  identifierLists: readonly (readonly string[])[],
): DependencyGraph {
  const canonicalByLower = new Map(nodes.map(n => [n.name.toLowerCase(), n.name] as const))
  const edges: DependencyEdge[] = []
  const edgeKeys = new Set<string>()

  refs.forEach((ref, i) => {
    const resolved = dedupeCaseInsensitive(
      (identifierLists[i] ?? [])
        .map(id => canonicalByLower.get(baseIdentifierName(id).toLowerCase()))
        .filter((id): id is string => !!id),
    )

    if (ref.kind === 'submissionValidation') {
      // No single "owner" — chain consecutive referenced values instead of connecting every
      // pair, so one rule mentioning many values doesn't fan out O(n²).
      for (let j = 1; j < resolved.length; j++) {
        const [a, b] = [resolved[j - 1], resolved[j]]
        const key = `submissionValidation|${[a.toLowerCase(), b.toLowerCase()].sort().join('|')}`
        if (edgeKeys.has(key)) continue
        edgeKeys.add(key)
        edges.push({ kind: 'submissionValidation', from: a, to: b, expression: ref.expression, directed: false })
      }
      return
    }

    for (const from of resolved.filter(id => id.toLowerCase() !== ref.owner.toLowerCase())) {
      const key = `${ref.kind}|${from.toLowerCase()}|${ref.owner.toLowerCase()}`
      if (edgeKeys.has(key)) continue
      edgeKeys.add(key)
      edges.push({ kind: ref.kind, from, to: ref.owner, expression: ref.expression, directed: true })
    }
  })

  return { nodes, edges }
}

/**
 * Ask the server to parse every expression with the real NCalc engine (`POST
 * /api/expressions/dependencies`) and return the identifiers each one references, in the same
 * order. A parse failure (e.g. a rule mid-edit) resolves to an empty list for that entry rather
 * than throwing — one broken rule shouldn't blank out the rest of the diagram.
 */
async function fetchIdentifierLists(expressions: string[]): Promise<string[][]> {
  if (expressions.length === 0) return []
  const res = await api.post<{ results: ExpressionDependencyResult[] }>('/api/expressions/dependencies', { expressions })
  return res.results.map(r => r.identifiers ?? [])
}

/**
 * Build the dependency graph for `schema` via the server's authoritative NCalc parse. Nodes are
 * every usable value (blank/duplicate names dropped), ordered like the layout tree reads
 * (unassigned values first) so related values tend to land near each other around the diagram.
 */
export async function buildDependencyGraph(schema: Schema): Promise<DependencyGraph> {
  const { nodes, refs } = collectDependencyRules(schema)
  const identifierLists = await fetchIdentifierLists(refs.map(r => r.expression))
  return assembleDependencyGraph(nodes, refs, identifierLists)
}
