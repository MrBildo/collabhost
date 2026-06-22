import { POLL_INTERVALS } from '@/lib/constants'
import { describe, expect, test } from 'vitest'
import { dashboardEventsRefetchInterval } from './use-dashboard'

// FE-QRY-01: the events feed must keep polling after an error — a backed-off
// interval, never `false` (which latches polling off and goes dark until a full
// reload).
describe('dashboardEventsRefetchInterval', () => {
  test('polls at the normal cadence while healthy', () => {
    expect(dashboardEventsRefetchInterval('success')).toBe(POLL_INTERVALS.dashboard)
    expect(dashboardEventsRefetchInterval('pending')).toBe(POLL_INTERVALS.dashboard)
  })

  test('backs off (does not latch off) on error', () => {
    const interval = dashboardEventsRefetchInterval('error')
    expect(interval).toBe(POLL_INTERVALS.dashboardErrorBackoff)
    // The bug was returning `false`; assert we never disable polling.
    expect(interval).not.toBe(false)
    expect(interval).toBeGreaterThan(0)
  })

  test('the error backoff is slower than the healthy cadence', () => {
    expect(dashboardEventsRefetchInterval('error')).toBeGreaterThan(dashboardEventsRefetchInterval('success'))
  })
})
