import { AUTH_STORAGE_KEY } from '@/lib/constants'
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, test } from 'vitest'
import { useAuth } from './use-auth'

describe('useAuth', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
  })

  test('is unauthenticated when no key is stored', () => {
    const { result } = renderHook(() => useAuth())
    expect(result.current.isAuthenticated).toBe(false)
    expect(result.current.userKey).toBeNull()
  })

  test('login stores the key and flips isAuthenticated (same tab)', () => {
    const { result } = renderHook(() => useAuth())

    act(() => {
      result.current.login('01ABC')
    })

    expect(result.current.isAuthenticated).toBe(true)
    expect(result.current.userKey).toBe('01ABC')
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBe('01ABC')
  })

  test('logout clears the key and flips isAuthenticated (same tab)', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01ABC')
    const { result } = renderHook(() => useAuth())
    expect(result.current.isAuthenticated).toBe(true)

    act(() => {
      result.current.logout()
    })

    expect(result.current.isAuthenticated).toBe(false)
    expect(result.current.userKey).toBeNull()
  })

  test('cross-tab logout: a storage event clearing the key re-reads as unauthenticated (FE-AUTH-04)', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01ABC')
    const { result } = renderHook(() => useAuth())
    expect(result.current.isAuthenticated).toBe(true)

    // Another tab logged out: localStorage is already cleared, and the browser
    // dispatches a `storage` event to THIS tab. jsdom does not auto-fire it, so
    // we simulate the event the other tab would cause.
    act(() => {
      localStorage.removeItem(AUTH_STORAGE_KEY)
      window.dispatchEvent(new StorageEvent('storage', { key: AUTH_STORAGE_KEY, newValue: null }))
    })

    expect(result.current.isAuthenticated).toBe(false)
    expect(result.current.userKey).toBeNull()
  })

  test('cross-tab login: a storage event setting the key re-reads as authenticated (FE-AUTH-04)', () => {
    const { result } = renderHook(() => useAuth())
    expect(result.current.isAuthenticated).toBe(false)

    act(() => {
      localStorage.setItem(AUTH_STORAGE_KEY, '01XYZ')
      window.dispatchEvent(new StorageEvent('storage', { key: AUTH_STORAGE_KEY, newValue: '01XYZ' }))
    })

    expect(result.current.isAuthenticated).toBe(true)
    expect(result.current.userKey).toBe('01XYZ')
  })

  test('ignores storage events for unrelated keys (FE-AUTH-04)', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01ABC')
    const { result } = renderHook(() => useAuth())

    act(() => {
      localStorage.setItem('some-other-key', 'noise')
      window.dispatchEvent(new StorageEvent('storage', { key: 'some-other-key', newValue: 'noise' }))
    })

    // Auth state unchanged — the unrelated key must not trigger a re-read that
    // matters, and certainly must not drop the session.
    expect(result.current.isAuthenticated).toBe(true)
    expect(result.current.userKey).toBe('01ABC')
  })
})
