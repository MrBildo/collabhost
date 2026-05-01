import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-routes', () => ({
  useRoutes: vi.fn(),
  useReloadProxy: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

import type { RouteEntry, RouteListResponse } from '@/api/types'
import { useReloadProxy, useRoutes } from '@/hooks/use-routes'
import { RoutesPage } from './RoutesPage'

const mockUseRoutes = vi.mocked(useRoutes)
const mockUseReloadProxy = vi.mocked(useReloadProxy)

function makeRoute(overrides: Partial<RouteEntry> = {}): RouteEntry {
  return {
    appExternalId: 'app-id-1',
    appName: 'my-api',
    appDisplayName: 'My API',
    domain: 'my-api.collab.internal',
    target: 'localhost:5100',
    proxyMode: 'reverseProxy',
    https: true,
    enabled: true,
    isPortal: false,
    ...overrides,
  }
}

function makePortalRoute(overrides: Partial<RouteEntry> = {}): RouteEntry {
  return {
    appExternalId: '',
    appName: 'collabhost',
    appDisplayName: 'Collabhost Portal',
    domain: 'collabhost.collab.internal',
    target: 'localhost:58400',
    proxyMode: 'reverseProxy',
    https: true,
    enabled: true,
    isPortal: true,
    ...overrides,
  }
}

function setupDefaults() {
  mockUseRoutes.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useRoutes>)

  mockUseReloadProxy.mockReturnValue({
    mutate: vi.fn(),
    isPending: false,
    isError: false,
    isSuccess: false,
    error: null,
  } as unknown as ReturnType<typeof useReloadProxy>)
}

function renderWithRoutes(routes: RouteEntry[], baseDomain = 'collab.internal') {
  const data: RouteListResponse = { routes, baseDomain }
  mockUseRoutes.mockReturnValue({
    data,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useRoutes>)
  render(<RoutesPage />)
}

describe('RoutesPage', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('renders empty state when no routes are configured', () => {
    renderWithRoutes([])
    expect(screen.getByText('No routes configured')).toBeInTheDocument()
  })

  test('renders a normal app row with a navigable button', () => {
    renderWithRoutes([makeRoute()])
    const appButton = screen.getByRole('button', { name: 'My API' })
    expect(appButton).toBeInTheDocument()
  })

  test('Portal row renders app display name as plain text, not a button', () => {
    renderWithRoutes([makePortalRoute()])
    expect(screen.getByText('Collabhost Portal')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Collabhost Portal' })).toBeNull()
  })

  test('Portal row domain is rendered as an external link', () => {
    renderWithRoutes([makePortalRoute()])
    const domainLink = screen.getByRole('link', { name: 'collabhost.collab.internal' })
    expect(domainLink).toBeInTheDocument()
    expect(domainLink).toHaveAttribute('href', 'https://collabhost.collab.internal')
  })

  test('normal app row is not accidentally treated as Portal', () => {
    renderWithRoutes([makeRoute(), makePortalRoute()])
    // My API should still be a navigable button
    expect(screen.getByRole('button', { name: 'My API' })).toBeInTheDocument()
    // Portal should be plain text only
    expect(screen.queryByRole('button', { name: 'Collabhost Portal' })).toBeNull()
  })
})
