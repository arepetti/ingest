import { describe, expect, it } from 'vitest'
import { formatDurationInput, parseDurationMinutes } from './duration'

describe('parseDurationMinutes', () => {
  it('parses plain minutes', () => {
    expect(parseDurationMinutes('45')).toBe(45)
    expect(parseDurationMinutes('0')).toBe(0)
  })

  it('parses HH:mm', () => {
    expect(parseDurationMinutes('1:30')).toBe(90)
    expect(parseDurationMinutes('10:05')).toBe(605)
    expect(parseDurationMinutes('36:15')).toBe(2175) // hours aren't capped at 24
  })

  it('parses dd HH:mm', () => {
    expect(parseDurationMinutes('2 03:15')).toBe(2 * 1440 + 3 * 60 + 15)
    expect(parseDurationMinutes('1 00:00')).toBe(1440)
  })

  it('tolerates surrounding whitespace and extra internal spacing', () => {
    expect(parseDurationMinutes('  90  ')).toBe(90)
    expect(parseDurationMinutes(' 1:30 ')).toBe(90)
    expect(parseDurationMinutes('1   05:00')).toBe(1 * 1440 + 5 * 60)
  })

  it('returns null for blank input', () => {
    expect(parseDurationMinutes('')).toBeNull()
    expect(parseDurationMinutes('   ')).toBeNull()
  })

  it('returns null for an unrecognised or invalid shape', () => {
    expect(parseDurationMinutes('abc')).toBeNull()
    expect(parseDurationMinutes('1:60')).toBeNull() // minutes out of range
    expect(parseDurationMinutes('1:2:3')).toBeNull()
    expect(parseDurationMinutes('-5')).toBeNull()
  })
})

describe('formatDurationInput', () => {
  it('formats under an hour as plain minutes', () => {
    expect(formatDurationInput(0)).toBe('0')
    expect(formatDurationInput(45)).toBe('45')
  })

  it('formats under a day as H:mm', () => {
    expect(formatDurationInput(90)).toBe('1:30')
    expect(formatDurationInput(605)).toBe('10:05')
  })

  it('formats a day or more as d H:mm', () => {
    expect(formatDurationInput(1440)).toBe('1 0:00')
    expect(formatDurationInput(2 * 1440 + 3 * 60 + 15)).toBe('2 3:15')
  })

  it('round-trips through parseDurationMinutes', () => {
    for (const minutes of [0, 45, 90, 605, 1440, 2895]) {
      expect(parseDurationMinutes(formatDurationInput(minutes))).toBe(minutes)
    }
  })
})
