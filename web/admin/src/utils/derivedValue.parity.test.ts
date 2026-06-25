import { describe, expect, it } from 'vitest'
import { _callBuiltin } from './expression'
import { coerceDerivedValue } from './sampleRules'

describe('average() client/server parity', () => {
  it('averages plain numbers', () => {
    expect(_callBuiltin('average', [2, 4, 6])).toBe(4)
  })

  it('coerces booleans and ignores nulls', () => {
    const flag = true
    const missing = null
    expect(_callBuiltin('average', [2, flag, missing, 4])).toBe((2 + 1 + 4) / 3)
  })

  it('returns null when no numeric args remain', () => {
    expect(_callBuiltin('average', [])).toBeNull()
    expect(_callBuiltin('average', [null])).toBeNull()
  })

  it('rejects strings, dates, and non-finite numbers', () => {
    expect(() => _callBuiltin('average', ['x'])).toThrow(/numeric or boolean/)
    expect(() => _callBuiltin('average', [new Date()])).toThrow(/numeric or boolean/)
    expect(() => _callBuiltin('average', [Number.NaN])).toThrow(/numeric or boolean/)
    expect(() => _callBuiltin('average', [Number.POSITIVE_INFINITY])).toThrow(/numeric or boolean/)
  })
})

describe('coerceDerivedValue client/server parity', () => {
  it('coerces integer type', () => {
    expect(coerceDerivedValue(1.2, 'Integer')).toBe(1)
  })

  it('coerces string and boolean types', () => {
    expect(coerceDerivedValue('yes', 'String')).toBe('yes')
    expect(coerceDerivedValue(true, 'Boolean')).toBe(true)
  })

  it('coerces date type to ISO string', () => {
    const dt = new Date('2026-03-15T00:00:00.000Z')
    expect(coerceDerivedValue(dt, 'Date')).toBe('2026-03-15T00:00:00.000Z')
  })

  it('yields null for division-by-zero style non-finite numbers', () => {
    expect(coerceDerivedValue(Number.NaN, 'Number')).toBeNull()
    expect(coerceDerivedValue(Number.POSITIVE_INFINITY, 'Number')).toBeNull()
  })

  it('yields null when raw is null', () => {
    expect(coerceDerivedValue(null, 'Number')).toBeNull()
  })
})
