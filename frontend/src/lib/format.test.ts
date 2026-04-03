import { describe, expect, test } from 'vitest'
import { formatEnumLabel, formatMemory, formatStatus, formatUptime, formatUptimeLong } from './format'

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
