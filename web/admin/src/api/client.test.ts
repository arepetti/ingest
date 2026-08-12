import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { initializeI18n, setLocale } from '../i18n'
import {
  ApiError,
  formatApiError,
  localizeDiagnostic,
  localizeDiagnostics,
  type ApiDiagnostic,
} from './client'

const diagnostic = (
  code: string,
  message: string,
  params: Record<string, unknown> = {},
): ApiDiagnostic => ({ code, message, params })

describe('API diagnostic localization', () => {
  beforeAll(async () => {
    await initializeI18n()
  })

  beforeEach(async () => {
    await setLocale('en-US')
  })

  it('localizes a known ProblemDetails code', () => {
    const error = new ApiError(409, "Account 'service-a' already exists.", [], {
      code: 'accounts.already_exists',
      params: { accountName: 'service-a' },
    })

    expect(formatApiError(error)).toBe('Account “service-a” already exists.')
  })

  it('interpolates structured parameters', () => {
    expect(localizeDiagnostic(diagnostic(
      'submissions.value_minimum',
      "Value 'Headcount' below min (5).",
      { displayName: 'Weekly / Headcount', minimum: 5, actual: 3 },
    ))).toBe('Value “Weekly / Headcount” must be at least 5 (received 3).')
  })

  it('formats multiple error details in order', () => {
    const details = [
      diagnostic('submissions.value_required', 'Headcount is required.', {
        displayName: 'Weekly / Headcount',
      }),
      diagnostic('submissions.schema_not_assigned', 'Schema missing.', {
        schemaName: 'finance',
      }),
    ]
    const error = new ApiError(400, 'Validation failed', ['Headcount is required.', 'Schema missing.'], {
      code: 'common.validation',
      errorDetails: details,
    })

    expect(formatApiError(error)).toBe([
      'Value “Weekly / Headcount” is required.',
      'Schema “finance” is not assigned to this service.',
    ].join('\n'))
  })

  it('falls back to server text for an unknown code without exposing the code', () => {
    const value = localizeDiagnostic(
      diagnostic('future.secret_code', 'The server supplied this explanation.'),
    )

    expect(value).toBe('The server supplied this explanation.')
    expect(value).not.toContain('future.secret_code')
  })

  it('keeps server-supplied text visible for generic coded categories', () => {
    expect(localizeDiagnostic(diagnostic(
      'common.conflict',
      'The operator supplied this actionable explanation.',
      { domain: 'submissions' },
    ))).toBe(
      'The request conflicts with the current state. The operator supplied this actionable explanation.',
    )
  })

  it('preserves legacy-only ProblemDetails errors', () => {
    const error = new ApiError(400, 'Validation failed', [
      'Name is required.',
      'Date is outside the allowed period.',
    ])

    expect(formatApiError(error)).toBe('Name is required.\nDate is outside the allowed period.')
  })

  it('localizes success diagnostics and keeps unmatched legacy entries', () => {
    const values = localizeDiagnostics(
      [diagnostic('submissions.warning_rule_triggered', 'Warning fired.', {
        displayName: 'Weekly / Headcount',
      })],
      ['Warning fired.', 'A legacy-only warning.'],
    )

    expect(values).toEqual([
      'The warning rule for “Weekly / Headcount” was triggered.',
      'A legacy-only warning.',
    ])
  })

  it('uses the active locale after locale switching', async () => {
    await setLocale('it-IT')

    expect(localizeDiagnostic(diagnostic(
      'accounts.already_exists',
      "Account 'servizio-a' already exists.",
      { accountName: 'servizio-a' },
    ))).toBe("L'account “servizio-a” esiste già.")
  })
})
