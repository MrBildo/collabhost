import { describe, expect, test } from 'vitest'
import {
  formatBytes,
  formatEnumLabel,
  formatHealthStatus,
  formatMemory,
  formatProxyState,
  formatStatus,
  formatUptime,
  formatUptimeLong,
  proxyStateDetail,
  toSlug,
} from './format'

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
    expect(formatStatus('backoff')).toBe('Backoff')
    expect(formatStatus('fatal')).toBe('Fatal')
  })
})

describe('formatHealthStatus', () => {
  test('formats managed-app statuses with healthy/unhealthy labels', () => {
    expect(formatHealthStatus('healthy')).toBe('Healthy')
    expect(formatHealthStatus('unhealthy')).toBe('Unhealthy')
    expect(formatHealthStatus('degraded')).toBe('Degraded')
    expect(formatHealthStatus('unknown')).toBe('Unknown')
  })

  test('formats managed-app statuses when slug is a known managed type', () => {
    expect(formatHealthStatus('healthy', 'dotnet-app')).toBe('Healthy')
    expect(formatHealthStatus('unhealthy', 'nodejs-app')).toBe('Unhealthy')
    expect(formatHealthStatus('healthy', 'static-site')).toBe('Healthy')
    expect(formatHealthStatus('healthy', 'executable')).toBe('Healthy')
    expect(formatHealthStatus('healthy', 'system-service')).toBe('Healthy')
  })

  test('formats external-route statuses with reachable/unreachable labels (Card #348 D6)', () => {
    expect(formatHealthStatus('healthy', 'external-route')).toBe('Reachable')
    expect(formatHealthStatus('unhealthy', 'external-route')).toBe('Unreachable')
  })

  test('preserves degraded and unknown labels for external-route (only reachability splits)', () => {
    expect(formatHealthStatus('degraded', 'external-route')).toBe('Degraded')
    expect(formatHealthStatus('unknown', 'external-route')).toBe('Unknown')
  })

  test('falls back to managed labels for an unknown slug', () => {
    expect(formatHealthStatus('healthy', 'some-future-type')).toBe('Healthy')
    expect(formatHealthStatus('unhealthy', 'some-future-type')).toBe('Unhealthy')
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

describe('formatBytes', () => {
  test('returns -- for null', () => {
    expect(formatBytes(null)).toBe('--')
  })

  test('returns -- for undefined', () => {
    expect(formatBytes(undefined)).toBe('--')
  })

  test('formats bytes under 1 KB', () => {
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(1023)).toBe('1023 B')
  })

  test('formats kilobytes', () => {
    expect(formatBytes(1024)).toBe('1.0 KB')
    expect(formatBytes(1024 * 512)).toBe('512.0 KB')
  })

  test('formats megabytes', () => {
    expect(formatBytes(1024 * 1024)).toBe('1.0 MB')
    expect(formatBytes(1024 * 1024 * 5)).toBe('5.0 MB')
  })

  test('formats gigabytes', () => {
    expect(formatBytes(1024 * 1024 * 1024)).toBe('1.00 GB')
    expect(formatBytes(1024 * 1024 * 1024 * 2.5)).toBe('2.50 GB')
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

describe('formatProxyState', () => {
  test('capitalizes all six states', () => {
    expect(formatProxyState('starting')).toBe('Starting')
    expect(formatProxyState('running')).toBe('Running')
    expect(formatProxyState('degraded')).toBe('Degraded')
    expect(formatProxyState('failed')).toBe('Failed')
    expect(formatProxyState('disabled')).toBe('Disabled')
    expect(formatProxyState('stopped')).toBe('Stopped')
  })
})

describe('proxyStateDetail', () => {
  test('running has no detail (healthy steady state)', () => {
    expect(proxyStateDetail('running')).toBeUndefined()
  })

  test('degraded names the operator-facing reason', () => {
    expect(proxyStateDetail('degraded')).toBe('Routes not reaching public listener')
  })

  test('failed names the operator action', () => {
    expect(proxyStateDetail('failed')).toBe('Check logs, restart Collabhost')
  })

  test('disabled points at the env var and installer', () => {
    expect(proxyStateDetail('disabled')).toBe('Re-run the installer or set COLLABHOST_PROXY_BINARY_PATH')
  })

  test('starting is transient warm-up text', () => {
    expect(proxyStateDetail('starting')).toBe('Warming up')
  })

  test('stopped is informational', () => {
    expect(proxyStateDetail('stopped')).toBe('Proxy app stopped')
  })
})
