import { describe, expect, it } from 'vitest'
import { resolveLocalePreference } from './resolution'

const supported = ['en-US', 'fr-FR']

describe('resolveLocalePreference', () => {
  it('prefers a valid saved locale over the configured default', () => {
    expect(resolveLocalePreference(' fr-fr ', 'en-US', supported, 'en-US')).toBe('fr-FR')
  })

  it('uses the configured default when the saved locale is unsupported', () => {
    expect(resolveLocalePreference('de-DE', 'FR-fr', supported, 'en-US')).toBe('fr-FR')
  })

  it('falls back safely when neither candidate is supported', () => {
    expect(resolveLocalePreference(null, 'de-DE', supported, 'en-US')).toBe('en-US')
  })
})
