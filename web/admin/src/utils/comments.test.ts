import { describe, expect, it } from 'vitest'
import { threadScopeLabel, isOwnComment } from './comments'
import type { SchemaValue } from '../api/types'

function value(overrides: Partial<SchemaValue>): SchemaValue {
  return {
    name: 'count',
    type: 'Integer',
    cadence: 'Monthly',
    required: true,
    modifiable: true,
    enabled: true,
    ...overrides,
  }
}

describe('threadScopeLabel', () => {
  const values = [value({ name: 'count', label: 'Item count' }), value({ name: 'unlabelled_value' })]

  it('returns "General" for a schema-level thread (no valueName)', () => {
    expect(threadScopeLabel({ valueName: null }, values)).toBe('General')
    expect(threadScopeLabel({ valueName: undefined }, values)).toBe('General')
  })

  it('returns the matching value\'s label', () => {
    expect(threadScopeLabel({ valueName: 'count' }, values)).toBe('Item count')
  })

  it('falls back to the value\'s machine name when it has no label', () => {
    expect(threadScopeLabel({ valueName: 'unlabelled_value' }, values)).toBe('unlabelled_value')
  })

  it('falls back to the raw stored name when the value can no longer be found', () => {
    expect(threadScopeLabel({ valueName: 'renamed_or_removed' }, values)).toBe('renamed_or_removed')
  })
})

describe('isOwnComment', () => {
  it('is true when the account id matches', () => {
    expect(isOwnComment({ createdByAccountId: 'acc-1' }, 'acc-1')).toBe(true)
  })

  it('is false when the account id differs', () => {
    expect(isOwnComment({ createdByAccountId: 'acc-1' }, 'acc-2')).toBe(false)
  })

  it('is false when either id is missing', () => {
    expect(isOwnComment({ createdByAccountId: null }, 'acc-1')).toBe(false)
    expect(isOwnComment({ createdByAccountId: 'acc-1' }, undefined)).toBe(false)
    expect(isOwnComment({ createdByAccountId: undefined }, undefined)).toBe(false)
  })
})
