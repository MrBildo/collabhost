import { describe, expect, test } from 'vitest'
import { formatActionError } from './format-action-error'

describe('formatActionError', () => {
  test('prefixes with the verb when provided', () => {
    const err = new Error('API 500: kaboom')
    expect(formatActionError(err, 'Start')).toBe('Start failed: server error (500) — kaboom')
  })

  test('uses generic prefix when no verb is provided', () => {
    const err = new Error('API 500: kaboom')
    expect(formatActionError(err)).toBe('Action failed: server error (500) — kaboom')
  })

  test('formats 409 as a state-conflict message', () => {
    const err = new Error('API 409: app is already running')
    expect(formatActionError(err, 'Start')).toBe('Start failed: state conflict — app is already running')
  })

  test('formats 409 with no body cleanly', () => {
    const err = new Error('API 409: ')
    expect(formatActionError(err, 'Stop')).toBe('Stop failed: state conflict')
  })

  test('formats 404 as app-not-found', () => {
    const err = new Error('API 404: ')
    expect(formatActionError(err, 'Start')).toBe('Start failed: app not found')
  })

  test('formats 403 as not-authorized', () => {
    const err = new Error('API 403: ')
    expect(formatActionError(err, 'Restart')).toBe('Restart failed: not authorized')
  })

  test('passes through 4xx body for unrecognised codes', () => {
    const err = new Error('API 422: bad input')
    expect(formatActionError(err, 'Kill')).toBe('Kill failed: bad input')
  })

  test('falls back to HTTP <code> when 4xx body is empty', () => {
    const err = new Error('API 418: ')
    expect(formatActionError(err, 'Start')).toBe('Start failed: HTTP 418')
  })

  test('handles errors that do not match the ApiError shape', () => {
    const err = new Error('network down')
    expect(formatActionError(err, 'Start')).toBe('Start failed: network down')
  })

  test('handles non-Error throwables gracefully', () => {
    expect(formatActionError('whoops', 'Start')).toBe('Start failed')
    expect(formatActionError(null)).toBe('Action failed')
  })

  test('preserves multi-line bodies', () => {
    const err = new Error('API 500: line1\nline2')
    expect(formatActionError(err, 'Start')).toBe('Start failed: server error (500) — line1\nline2')
  })
})
