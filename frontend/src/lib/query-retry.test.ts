import { ApiError } from '@/api/client'
import { describe, expect, test } from 'vitest'
import { isRetryableError, shouldRetryQuery } from './query-retry'

describe('isRetryableError', () => {
  test('a 5xx ApiError is retryable', () => {
    expect(isRetryableError(new ApiError(500, 'boom'))).toBe(true)
    expect(isRetryableError(new ApiError(503, 'unavailable'))).toBe(true)
  })

  test('a 4xx ApiError is not retryable', () => {
    expect(isRetryableError(new ApiError(400, 'bad request'))).toBe(false)
    expect(isRetryableError(new ApiError(401, 'expired'))).toBe(false)
    expect(isRetryableError(new ApiError(403, 'forbidden'))).toBe(false)
    expect(isRetryableError(new ApiError(404, 'not found'))).toBe(false)
  })

  test('a non-ApiError (network / parse failure) is retryable', () => {
    expect(isRetryableError(new TypeError('Failed to fetch'))).toBe(true)
    expect(isRetryableError(new Error('something'))).toBe(true)
  })
})

describe('shouldRetryQuery', () => {
  test('retries a 5xx exactly once (failureCount 0 then stops at 1)', () => {
    const err = new ApiError(500, 'boom')
    expect(shouldRetryQuery(0, err)).toBe(true)
    expect(shouldRetryQuery(1, err)).toBe(false)
  })

  test('never retries a 4xx, even on the first failure', () => {
    expect(shouldRetryQuery(0, new ApiError(404, 'not found'))).toBe(false)
    expect(shouldRetryQuery(0, new ApiError(400, 'bad request'))).toBe(false)
  })

  test('retries a network failure once', () => {
    expect(shouldRetryQuery(0, new TypeError('Failed to fetch'))).toBe(true)
    expect(shouldRetryQuery(1, new TypeError('Failed to fetch'))).toBe(false)
  })
})
