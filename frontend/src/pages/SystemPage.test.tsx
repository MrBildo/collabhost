import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-system-status', () => ({
  useSystemStatus: vi.fn(),
}))

import type { ProxyDetail, ProxyState, SystemStatus } from '@/api/types'
import { useSystemStatus } from '@/hooks/use-system-status'
import { SystemPage } from './SystemPage'

const mockUseSystemStatus = vi.mocked(useSystemStatus)

function makeStatus(overrides: Partial<SystemStatus> = {}): SystemStatus {
  return {
    status: 'ok',
    version: '1.0.0',
    hostname: 'homelab',
    uptimeSeconds: 12345,
    timestamp: '2026-05-03T10:00:00.000Z',
    proxyState: 'running',
    portalUrl: 'https://collabhost.collab.internal',
    portalReachable: true,
    proxyDetail: null,
    ...overrides,
  }
}

function makeProxyDetail(overrides: Partial<ProxyDetail> = {}): ProxyDetail {
  return {
    lastSyncOk: false,
    lastSyncError: 'Caddy admin API returned 400: loading config: listening on :443: bind: permission denied',
    lastSyncAt: '2026-05-03T09:58:42Z',
    listenAddress: ':80,:443',
    ...overrides,
  }
}

function setStatus(status: SystemStatus | undefined, isLoading = false) {
  mockUseSystemStatus.mockReturnValue({
    data: status,
    isLoading,
    error: null,
  } as unknown as ReturnType<typeof useSystemStatus>)
}

describe('SystemPage', () => {
  beforeEach(() => {
    setStatus(makeStatus())
  })

  test('renders Portal URL as bright amber link when portalReachable is true', () => {
    setStatus(makeStatus({ portalReachable: true }))
    render(<SystemPage />)

    const link = screen.getByRole('link', { name: 'https://collabhost.collab.internal' })
    expect(link).toHaveStyle({ color: 'var(--wm-amber)' })
    expect(screen.queryByText(/not reachable/i)).toBeNull()
  })

  test('renders Portal URL muted with annotation when portalReachable is false (degraded)', () => {
    setStatus(
      makeStatus({
        proxyState: 'degraded',
        portalReachable: false,
        proxyDetail: makeProxyDetail(),
      }),
    )
    render(<SystemPage />)

    const link = screen.getByRole('link', { name: 'https://collabhost.collab.internal' })
    expect(link).toHaveStyle({ color: 'var(--wm-text-dim)' })
    expect(screen.getByText(/not reachable while proxy is degraded/i)).toBeInTheDocument()
  })

  test('renders proxyDetail block when present (degraded)', () => {
    setStatus(
      makeStatus({
        proxyState: 'degraded',
        portalReachable: false,
        proxyDetail: makeProxyDetail(),
      }),
    )
    render(<SystemPage />)

    expect(screen.getByTestId('proxy-detail-panel')).toBeInTheDocument()
    expect(screen.getByText('Proxy Detail')).toBeInTheDocument()
    expect(screen.getByText('Listen')).toBeInTheDocument()
    expect(screen.getByText(':80,:443')).toBeInTheDocument()
    expect(screen.getByText(/loading config: listening on :443: bind: permission denied/)).toBeInTheDocument()
    expect(screen.getByText('failed')).toBeInTheDocument()
  })

  test('renders sync-succeeded state when lastSyncOk is true', () => {
    setStatus(
      makeStatus({
        proxyState: 'running',
        portalReachable: true,
        proxyDetail: makeProxyDetail({ lastSyncOk: true, lastSyncError: null }),
      }),
    )
    render(<SystemPage />)

    expect(screen.getByTestId('proxy-detail-panel')).toBeInTheDocument()
    expect(screen.getByText('succeeded')).toBeInTheDocument()
    // No Error row when lastSyncError is null
    expect(screen.queryByText('Error')).toBeNull()
  })

  test('does NOT render proxyDetail block when proxyDetail is null (running, healthy)', () => {
    setStatus(makeStatus({ proxyState: 'running', portalReachable: true, proxyDetail: null }))
    render(<SystemPage />)

    expect(screen.queryByTestId('proxy-detail-panel')).toBeNull()
    expect(screen.queryByText('Proxy Detail')).toBeNull()
  })

  test('renders proxy state row with degraded label and detail in the system table', () => {
    setStatus(
      makeStatus({
        proxyState: 'degraded',
        portalReachable: false,
        proxyDetail: makeProxyDetail(),
      }),
    )
    render(<SystemPage />)

    // 'Degraded' appears in both the StatusStrip cell and the table row
    expect(screen.getAllByText('Degraded').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('Routes not reaching public listener').length).toBeGreaterThanOrEqual(1)
  })

  test.each<ProxyState>(['running', 'starting', 'stopped', 'disabled'])(
    'does NOT render proxyDetail block for %s state when proxyDetail is null',
    (state) => {
      setStatus(makeStatus({ proxyState: state, proxyDetail: null }))
      render(<SystemPage />)
      expect(screen.queryByTestId('proxy-detail-panel')).toBeNull()
    },
  )
})
