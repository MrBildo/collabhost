import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'

// Mock the endpoint layer — the hook-level race (#409) is about how the query
// client treats the settings query on delete, not about the HTTP wire. We count
// getAppSettings calls and make deleteApp resolve.
vi.mock('@/api/endpoints', () => ({
  getAppSettings: vi.fn(),
  deleteApp: vi.fn(),
  updateAppSettings: vi.fn(),
  importRuntimeConfigFile: vi.fn(),
  restartApp: vi.fn(),
}))

import { deleteApp, getAppSettings } from '@/api/endpoints'
import type { AppSettings } from '@/api/types'
import { useAppSettings, useDeleteApp } from './use-app-settings'

const mockGetAppSettings = vi.mocked(getAppSettings)
const mockDeleteApp = vi.mocked(deleteApp)

function makeSettings(): AppSettings {
  return {
    id: 'app-1',
    name: 'doomed-app',
    displayName: 'Doomed App',
    appTypeName: 'Static Site',
    registeredAt: '2026-06-01T00:00:00Z',
    sections: [],
  }
}

let queryClient: QueryClient

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
}

beforeEach(() => {
  queryClient = new QueryClient({
    defaultOptions: {
      // No retries — a 404 in this test must surface as a single call, not three.
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  mockGetAppSettings.mockReset()
  mockDeleteApp.mockReset()
  mockGetAppSettings.mockResolvedValue(makeSettings())
  mockDeleteApp.mockResolvedValue(undefined)
})

afterEach(() => {
  queryClient.clear()
  vi.restoreAllMocks()
})

describe('useDeleteApp — #409 delete-redirect settings refetch race', () => {
  test('does not re-fetch the deleted app’s settings after delete (no 404 race)', async () => {
    const slug = 'doomed-app'

    // An active useAppSettings observer is mounted — this is the AppSettingsPage's
    // live settings query. It fetches once on mount. It stays mounted across the
    // delete (the real component navigates away in its own onSuccess, but the
    // mutation-hook cache cleanup + the page's redirect-transition render happen
    // while the observer is still subscribed — the window the 404 fired in, per
    // the S89 e2e: `404 on GET /api/v1/apps/<slug>/settings`).
    const settings = renderHook(() => useAppSettings(slug), { wrapper })
    const del = renderHook(() => useDeleteApp(), { wrapper })

    await waitFor(() => expect(settings.result.current.isSuccess).toBe(true))
    expect(mockGetAppSettings).toHaveBeenCalledTimes(1)

    // Delete the app, then force the render the navigation transition causes while
    // the settings observer is still subscribed. The faithful failure mode: the
    // delete onSuccess invalidates the `['apps']` prefix, which (unless scoped)
    // marks the still-active `['apps', slug, 'settings']` query stale and refetches
    // it against the now-deleted resource → 404. (Probed directly: an `['apps']`
    // prefix invalidate refetches an active `['apps', slug, 'settings']` observer.)
    await act(async () => {
      await del.result.current.mutateAsync(slug)
    })
    await act(async () => {
      settings.rerender()
      await Promise.resolve()
    })

    expect(mockDeleteApp).toHaveBeenCalledTimes(1)

    // The deleted app's /settings must NOT be requested a second time.
    expect(mockGetAppSettings).toHaveBeenCalledTimes(1)
  })
})
