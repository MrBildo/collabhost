import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'

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

function renderWithRoutes(
  routes: RouteEntry[],
  baseDomain = 'collab.internal',
  overrides: Partial<Pick<RouteListResponse, 'proxyState' | 'portalReachable'>> = {},
) {
  const data: RouteListResponse = {
    routes,
    baseDomain,
    proxyState: overrides.proxyState ?? 'running',
    portalReachable: overrides.portalReachable ?? true,
  }
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

  test('Portal row domain renders muted with not-reachable annotation when portalReachable is false', () => {
    renderWithRoutes([makePortalRoute()], 'collab.internal', {
      proxyState: 'degraded',
      portalReachable: false,
    })

    const portalDomainLink = screen.getByTestId('portal-row-domain-unreachable')
    expect(portalDomainLink).toBeInTheDocument()
    expect(portalDomainLink).toHaveAttribute('href', 'https://collabhost.collab.internal')
    expect(portalDomainLink).toHaveStyle({ color: 'var(--wm-text-dim)' })
    expect(screen.getByText(/not reachable/i)).toBeInTheDocument()
  })

  test('Portal row domain stays bright when portalReachable is true (running)', () => {
    renderWithRoutes([makePortalRoute()], 'collab.internal', {
      proxyState: 'running',
      portalReachable: true,
    })

    expect(screen.queryByTestId('portal-row-domain-unreachable')).toBeNull()
    const domainLink = screen.getByRole('link', { name: 'collabhost.collab.internal' })
    expect(domainLink).toHaveStyle({ color: 'var(--wm-text-bright)' })
  })

  test('non-Portal app row is unaffected by portalReachable', () => {
    renderWithRoutes([makeRoute()], 'collab.internal', {
      proxyState: 'degraded',
      portalReachable: false,
    })

    // The regular app row's domain still renders as the normal bright link
    expect(screen.queryByTestId('portal-row-domain-unreachable')).toBeNull()
    const domainLink = screen.getByRole('link', { name: 'my-api.collab.internal' })
    expect(domainLink).toHaveStyle({ color: 'var(--wm-text-bright)' })
  })

  test('renders the proxy-state health cell (FE-XT-06)', () => {
    renderWithRoutes([makeRoute()], 'collab.internal', { proxyState: 'running' })

    // buildProxyStateCell labels the cell "Proxy" and formats the state value.
    expect(screen.getByText('Proxy')).toBeInTheDocument()
    expect(screen.getByText('Running')).toBeInTheDocument()
  })

  test('surfaces a degraded proxy state on the routing page (FE-XT-06)', () => {
    renderWithRoutes([makeRoute()], 'collab.internal', { proxyState: 'degraded' })

    expect(screen.getByText('Proxy')).toBeInTheDocument()
    expect(screen.getByText('Degraded')).toBeInTheDocument()
  })
})

describe('RoutesPage reload-success auto-clear (FE-UI-07)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    setupDefaults()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  test('the success banner shows then auto-clears via reset() after the timeout', () => {
    const reset = vi.fn()
    mockUseReloadProxy.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
      isError: false,
      isSuccess: true,
      error: null,
      reset,
    } as unknown as ReturnType<typeof useReloadProxy>)
    mockUseRoutes.mockReturnValue({
      data: { routes: [makeRoute()], baseDomain: 'collab.internal', proxyState: 'running', portalReachable: true },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useRoutes>)

    render(<RoutesPage />)

    expect(screen.getByText('Proxy configuration reloaded successfully.')).toBeInTheDocument()
    expect(reset).not.toHaveBeenCalled()

    act(() => {
      vi.advanceTimersByTime(4_000)
    })

    // The component drives the dismissal by resetting the mutation; the banner
    // disappears on the next render the reset triggers (asserted via reset call).
    expect(reset).toHaveBeenCalledOnce()
  })

  test('does not schedule a reset when the reload has not succeeded', () => {
    const reset = vi.fn()
    mockUseReloadProxy.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
      isError: false,
      isSuccess: false,
      error: null,
      reset,
    } as unknown as ReturnType<typeof useReloadProxy>)
    mockUseRoutes.mockReturnValue({
      data: { routes: [makeRoute()], baseDomain: 'collab.internal', proxyState: 'running', portalReachable: true },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useRoutes>)

    render(<RoutesPage />)

    act(() => {
      vi.advanceTimersByTime(4_000)
    })

    expect(reset).not.toHaveBeenCalled()
  })
})
