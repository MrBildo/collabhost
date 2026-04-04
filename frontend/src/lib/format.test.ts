import { describe, expect, test } from 'vitest'
import { formatEnumLabel, formatMemory, formatStatus, formatUptime, formatUptimeLong, toSlug } from './format'

describe('formatUptime', () => {
  test('returns -- for null', () => {
    expect(formatUptime(null)).toBe('--')
  })

  test('returns -- for undefined', () => {
    expect(formatUptime(undefined)).toBe('--')
  })

  test('formats seconds', () => {
    expect(formatUptime(45)).toBe('45s')
  })

  test('formats minutes', () => {
    expect(formatUptime(125)).toBe('2m')
  })

  test('formats hours', () => {
    expect(formatUptime(7200)).toBe('2h')
  })

  test('formats days', () => {
    expect(formatUptime(172800)).toBe('2d')
  })

  test('rounds fractional seconds', () => {
    expect(formatUptime(13.7)).toBe('14s')
  })
})

describe('formatUptimeLong', () => {
  test('returns -- for null', () => {
    expect(formatUptimeLong(null)).toBe('--')
  })

  test('formats days and hours', () => {
    expect(formatUptimeLong(90000)).toBe('1d 1h')
  })

  test('formats hours and minutes', () => {
    expect(formatUptimeLong(3720)).toBe('1h 2m')
  })

  test('formats minutes and seconds', () => {
    expect(formatUptimeLong(125)).toBe('2m 5s')
  })

  test('formats seconds only', () => {
    expect(formatUptimeLong(45)).toBe('45s')
  })

  test('rounds fractional seconds', () => {
    expect(formatUptimeLong(613.9)).toBe('10m 14s')
  })

  test('rounds sub-minute fractional seconds', () => {
    expect(formatUptimeLong(13.7)).toBe('14s')
  })
})

describe('formatStatus', () => {
  test('formats known statuses', () => {
    expect(formatStatus('running')).toBe('Running')
    expect(formatStatus('stopped')).toBe('Stopped')
    expect(formatStatus('crashed')).toBe('Crashed')
    expect(formatStatus('starting')).toBe('Starting')
    expect(formatStatus('stopping')).toBe('Stopping')
    expect(formatStatus('restarting')).toBe('Restarting')
  })
})

describe('formatEnumLabel', () => {
  test('converts camelCase to title case with spaces', () => {
    expect(formatEnumLabel('onCrash')).toBe('On Crash')
    expect(formatEnumLabel('reverseProxy')).toBe('Reverse Proxy')
  })

  test('capitalizes first letter', () => {
    expect(formatEnumLabel('never')).toBe('Never')
  })

  test('handles single word', () => {
    expect(formatEnumLabel('always')).toBe('Always')
  })
})

describe('formatMemory', () => {
  test('returns -- for null', () => {
    expect(formatMemory(null)).toBe('--')
  })

  test('formats megabytes', () => {
    expect(formatMemory(512)).toBe('512 MB')
  })

  test('formats gigabytes', () => {
    expect(formatMemory(2048)).toBe('2.0 GB')
  })
})

describe('toSlug', () => {
  test('lowercases and replaces spaces with hyphens', () => {
    expect(toSlug('My Cool App')).toBe('my-cool-app')
  })

  test('replaces underscores with hyphens', () => {
    expect(toSlug('my_cool_app')).toBe('my-cool-app')
  })

  test('strips special characters', () => {
    expect(toSlug("Bill's App (v2)")).toBe('bills-app-v2')
  })

  test('collapses multiple hyphens', () => {
    expect(toSlug('my - - app')).toBe('my-app')
  })

  test('trims leading and trailing hyphens', () => {
    expect(toSlug('--my-app--')).toBe('my-app')
  })

  test('handles empty string', () => {
    expect(toSlug('')).toBe('')
  })

  test('handles all-special-characters input', () => {
    expect(toSlug('!@#$%')).toBe('')
  })

  test('truncates to 63 characters', () => {
    const long = 'a'.repeat(100)
    expect(toSlug(long)).toBe('a'.repeat(63))
  })

  test('strips trailing hyphen after truncation', () => {
    const input = `${'a'.repeat(62)}-bcd`
    expect(toSlug(input).endsWith('-')).toBe(false)
  })

  test('handles mixed case with numbers', () => {
    expect(toSlug('Collabhost V2 API')).toBe('collabhost-v2-api')
  })
})
