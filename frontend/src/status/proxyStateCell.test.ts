import type { ProxyState } from '@/api/types'
import { describe, expect, test } from 'vitest'
import { buildProxyStateCell, proxyStateColor } from './proxyStateCell'

describe('proxyStateColor', () => {
  test('maps running to green', () => {
    expect(proxyStateColor('running')).toBe('green')
  })

  test('maps failed to red', () => {
    expect(proxyStateColor('failed')).toBe('red')
  })

  test('maps disabled to amber', () => {
    expect(proxyStateColor('disabled')).toBe('amber')
  })

  test('maps starting to amber (transient)', () => {
    expect(proxyStateColor('starting')).toBe('amber')
  })

  test('maps stopped to default (neutral)', () => {
    expect(proxyStateColor('stopped')).toBe('default')
  })
})

describe('buildProxyStateCell', () => {
  test('returns Proxy label regardless of state', () => {
    const states: ProxyState[] = ['starting', 'running', 'failed', 'disabled', 'stopped']
    for (const state of states) {
      expect(buildProxyStateCell(state).label).toBe('Proxy')
    }
  })

  test('running cell: green, no detail (healthy steady state)', () => {
    const cell = buildProxyStateCell('running')
    expect(cell.value).toBe('Running')
    expect(cell.color).toBe('green')
    expect(cell.detail).toBeUndefined()
  })

  test('starting cell: amber, "Warming up" detail', () => {
    const cell = buildProxyStateCell('starting')
    expect(cell.value).toBe('Starting')
    expect(cell.color).toBe('amber')
    expect(cell.detail).toBe('Warming up')
  })

  test('failed cell: red, remediation detail', () => {
    const cell = buildProxyStateCell('failed')
    expect(cell.value).toBe('Failed')
    expect(cell.color).toBe('red')
    expect(cell.detail).toBe('Check logs, restart Collabhost')
  })

  test('disabled cell: amber, actionable remediation detail', () => {
    const cell = buildProxyStateCell('disabled')
    expect(cell.value).toBe('Disabled')
    expect(cell.color).toBe('amber')
    expect(cell.detail).toBe('Re-run the installer or set COLLABHOST_CADDY_PATH')
  })

  test('stopped cell: default color, informational detail', () => {
    const cell = buildProxyStateCell('stopped')
    expect(cell.value).toBe('Stopped')
    expect(cell.color).toBe('default')
    expect(cell.detail).toBe('Proxy app stopped')
  })
})
