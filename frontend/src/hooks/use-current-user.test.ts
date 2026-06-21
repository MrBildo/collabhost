import { ApiError } from '@/api/client'
import { describe, expect, test } from 'vitest'
import { shouldRetryIdentity } from './use-current-user'

// FE-AUTH-02: the identity query retries transient failures (so a blip
// self-heals instead of freezing sign-out / Users nav / PermissionGate) but
// must NOT retry an auth/permission failure — a 401 already clears the session
// via the client wrapper, and a 403 is a settled answer.
describe('shouldRetryIdentity', () => {
  test('retries a transient failure until the limit', () => {
    const err = new ApiError(500, 'boom')
    expect(shouldRetryIdentity(0, err)).toBe(true)
    expect(shouldRetryIdentity(1, err)).toBe(true)
  })

  test('stops retrying once the limit is reached', () => {
    const err = new ApiError(500, 'boom')
    expect(shouldRetryIdentity(2, err)).toBe(false)
  })

  test('retries a non-ApiError (e.g. network) transient failure', () => {
    expect(shouldRetryIdentity(0, new TypeError('Failed to fetch'))).toBe(true)
  })

  test('never retries a 401 — the session is already cleared', () => {
    expect(shouldRetryIdentity(0, new ApiError(401, 'Session expired. Please log in again.'))).toBe(false)
  })

  test('never retries a 403 — a forbidden is a settled answer', () => {
    expect(shouldRetryIdentity(0, new ApiError(403, 'forbidden'))).toBe(false)
  })
})
