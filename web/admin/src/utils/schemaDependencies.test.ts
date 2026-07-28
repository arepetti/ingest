import { describe, expect, it } from 'vitest'
import type { Schema, SchemaValue } from '../api/types'
import { emptySchema, emptyValue } from './schema'
import { assembleDependencyGraph, collectDependencyRules } from './schemaDependencies'

function value(overrides: Partial<SchemaValue>): SchemaValue {
  return { ...emptyValue(), ...overrides }
}

function schema(values: SchemaValue[], submissionValidations: string[] = []): Schema {
  const req = emptySchema()
  return {
    ...req,
    id: 'schema-1',
    values,
    submissionValidations,
    layout: [],
    version: 1,
    versionModifiedAt: null,
    createdAt: '',
    modifiedAt: '',
  }
}

describe('collectDependencyRules', () => {
  it('produces one node per usable value, dropping blank/duplicate names', () => {
    const { nodes } = collectDependencyRules(schema([
      value({ name: 'a', label: 'Alpha' }),
      value({ name: '' }),
      value({ name: 'a', label: 'Duplicate' }),
    ]))
    expect(nodes.map(n => n.name)).toEqual(['a'])
    expect(nodes[0].label).toBe('Alpha')
  })

  it('marks Calculated values without tagging the others', () => {
    const { nodes } = collectDependencyRules(schema([
      value({ name: 'peak' }),
      value({ name: 'ratio', kind: 'Calculated', expression: 'peak / 2' }),
    ]))
    expect(nodes.find(n => n.name === 'peak')?.calculated).toBe(false)
    expect(nodes.find(n => n.name === 'ratio')?.calculated).toBe(true)
  })

  it('collects one ref per non-blank rule field, tagged with its owner and kind', () => {
    const { refs } = collectDependencyRules(schema([
      value({ name: 'peak' }),
      value({
        name: 'ratio', kind: 'Calculated', expression: 'peak / 2',
        visibleIf: 'peak > 0', enabledIf: '', valueValidation: '', warning: '  ',
      }),
    ]))
    expect(refs).toEqual([
      { owner: 'ratio', kind: 'calculated', expression: 'peak / 2' },
      { owner: 'ratio', kind: 'visibleIf', expression: 'peak > 0' },
    ])
  })

  it('collects schema-level submissionValidations with a blank owner', () => {
    const { refs } = collectDependencyRules(schema([value({ name: 'a' })], ['a >= 0', '  ', '']))
    expect(refs).toEqual([{ owner: '', kind: 'submissionValidation', expression: 'a >= 0' }])
  })
})

describe('assembleDependencyGraph', () => {
  it('adds a directed calculated edge from each referenced sibling to the derived value', () => {
    const { nodes, refs } = collectDependencyRules(schema([
      value({ name: 'peak' }),
      value({ name: 'average' }),
      value({ name: 'ratio', kind: 'Calculated', expression: 'peak / average' }),
    ]))
    const g = assembleDependencyGraph(nodes, refs, [['peak', 'average']])
    expect(g.edges).toEqual(expect.arrayContaining([
      expect.objectContaining({ kind: 'calculated', from: 'peak', to: 'ratio', directed: true }),
      expect.objectContaining({ kind: 'calculated', from: 'average', to: 'ratio', directed: true }),
    ]))
  })

  it('drops identifiers the server returns that are not actual schema values', () => {
    const { nodes, refs } = collectDependencyRules(schema([
      value({ name: 'a' }),
      value({ name: 'total', kind: 'Calculated', expression: 'average(a, b, c)' }),
    ]))
    // The server reports every identifier it parsed, including function-call-adjacent names
    // that never resolve to a real value — those must not become phantom edges/nodes.
    const g = assembleDependencyGraph(nodes, refs, [['a', 'b', 'c']])
    expect(g.edges).toEqual([expect.objectContaining({ from: 'a', to: 'total' })])
  })

  it('never draws a self-loop when a rule references its own value', () => {
    const { nodes, refs } = collectDependencyRules(schema([
      value({ name: 'vehicle_breakdowns', warning: "if(vehicle_breakdowns >= 2, 'Two or more', null)" }),
    ]))
    const g = assembleDependencyGraph(nodes, refs, [['vehicle_breakdowns']])
    expect(g.edges).toEqual([])
  })

  it('resolves a [name.minimum]/[name.maximum] bound-key reference to the base value node', () => {
    const { nodes, refs } = collectDependencyRules(schema([
      value({ name: 'weight', min: 0 }),
      value({ name: 'gated', visibleIf: '[weight.minimum] < weight' }),
    ]))
    const g = assembleDependencyGraph(nodes, refs, [['weight.minimum', 'weight']])
    expect(g.edges).toEqual([expect.objectContaining({ kind: 'visibleIf', from: 'weight', to: 'gated' })])
  })

  it('chains schema-level submissionValidations into undirected edges between the values they mention', () => {
    const { nodes, refs } = collectDependencyRules(schema(
      [value({ name: 'peak' }), value({ name: 'average' })],
      ['peak >= average'],
    ))
    const g = assembleDependencyGraph(nodes, refs, [['peak', 'average']])
    expect(g.edges).toEqual([expect.objectContaining({ kind: 'submissionValidation', directed: false, expression: 'peak >= average' })])
    const edge = g.edges[0]
    expect([edge.from, edge.to].sort()).toEqual(['average', 'peak'])
  })

  it('deduplicates repeated references to the same value within one expression', () => {
    const { nodes, refs } = collectDependencyRules(schema([
      value({ name: 'a' }),
      value({ name: 'b', kind: 'Calculated', expression: 'a + a + a' }),
    ]))
    const g = assembleDependencyGraph(nodes, refs, [['a', 'a', 'a']])
    expect(g.edges.filter(e => e.kind === 'calculated')).toHaveLength(1)
  })

  it('treats a failed/empty parse (empty identifier list) as no dependency for that rule', () => {
    const { nodes, refs } = collectDependencyRules(schema([
      value({ name: 'a', visibleIf: 'not a real expression(' }),
    ]))
    const g = assembleDependencyGraph(nodes, refs, [[]])
    expect(g.edges).toEqual([])
    expect(g.nodes.map(n => n.name)).toEqual(['a'])
  })
})
