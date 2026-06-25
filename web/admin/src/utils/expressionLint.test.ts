import { describe, it, expect } from 'vitest'
import { findUnknownIdentifiers } from './expressionLint'

const known = new Set(['revenue', 'expenses'])
const messages = (text: string) => findUnknownIdentifiers(text, known).map(p => p.message)

describe('findUnknownIdentifiers', () => {
  it('flags an unknown variable', () => {
    expect(messages('if(foo > revenue, 1, 2)')).toEqual(["Unknown value 'foo'."])
  })

  it('flags an unknown function', () => {
    expect(messages('frobnicate(revenue)')).toEqual(["Unknown function 'frobnicate'."])
  })

  it('accepts known functions (built-in, native and context)', () => {
    expect(messages('average(revenue, expenses)')).toEqual([])
    expect(messages('round(revenue)')).toEqual([])
    expect(messages('latest("revenue")')).toEqual([])
  })

  it('accepts known variables and operators/literals', () => {
    expect(messages('revenue > expenses and true')).toEqual([])
    expect(messages('revenue in (1, 2, 3)')).toEqual([])
  })

  it('ignores text inside string and date literals', () => {
    expect(messages("revenue > 'foo bar baz'")).toEqual([])
    expect(messages('revenue > #2020-01-01#')).toEqual([])
  })

  it('ignores bracketed bound-key references', () => {
    expect(messages('[revenue.minimum] > 0')).toEqual([])
  })

  it('reports the correct offsets', () => {
    const probs = findUnknownIdentifiers('foo + 1', known)
    expect(probs).toEqual([{ from: 0, to: 3, message: "Unknown value 'foo'." }])
  })

  it('is case-insensitive for known names', () => {
    expect(messages('REVENUE > 0')).toEqual([])
  })
})
