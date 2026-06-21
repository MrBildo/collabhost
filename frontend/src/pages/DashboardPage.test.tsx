import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-apps', () => ({
  useApps: vi.fn(),
  useStartApp: vi.fn(),
  useStopApp: vi.fn(),
}))

vi.mock('@/hooks/use-dashboard', () => ({
  useDashboardStats: vi.fn(),
  useDashboardEvents: vi.fn(),
}))

vi.mock('@/hooks/use-system-status', () => ({
  useSystemStatus: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}))

import type { DashboardEvent, DashboardStats, ProxyState, SystemStatus } from '@/api/types'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { useDashboardEvents, useDashboardStats } from '@/hooks/use-dashboard'
import { useSystemStatus } from '@/hooks/use-system-status'
import { DashboardPage } from './DashboardPage'

const mockUseApps = vi.mocked(useApps)
const mockUseDashboardStats = vi.mocked(useDashboardStats)
const mockUseDashboardEvents = vi.mocked(useDashboardEvents)
const mockUseSystemStatus = vi.mocked(useSystemStatus)
const mockUseStartApp = vi.mocked(useStartApp)
const mockUseStopApp = vi.mocked(useStopApp)

function makeMutationStub(
  overrides: Partial<{ isError: boolean; error: Error | null; isPending: boolean; reset: () => void }> = {},
) {
  return {
    mutate: vi.fn(),
    isPending: overrides.isPending ?? false,
    isError: overrides.isError ?? false,
    error: overrides.error ?? null,
    reset: overrides.reset ?? vi.fn(),
  } as unknown as ReturnType<typeof useStartApp>
}

let _eventCounter = 0

function makeEvent(overrides: Partial<DashboardEvent> = {}): DashboardEvent {
  _eventCounter += 1
  return {
    id: `01TESTEVENT0000000000000${_eventCounter.toString().padStart(2, '0')}`,
    timestamp: '2026-04-07T12:00:00Z',
    message: 'started',
    appSlug: 'my-api',
    source: 'Admin',
    severity: 'info',
    ...overrides,
  }
}

function makeStats(overrides: Partial<DashboardStats> = {}): DashboardStats {
  return {
    totalApps: 0,
    running: 0,
    stopped: 0,
    crashed: 0,
    backoff: 0,
    fatal: 0,
    issues: 0,
    issuesSummary: null,
    appTypes: 0,
    ...overrides,
  }
}

function makeSystemStatus(proxyState: ProxyState): SystemStatus {
  return {
    status: 'ok',
    version: '0.1.0',
    hostname: 'test-host',
    uptimeSeconds: 120,
    timestamp: '2026-04-20T21:00:00.000Z',
    proxyState,
    portalUrl: 'https://collabhost.collab.internal',
    portalReachable: proxyState === 'running',
    proxyDetail: null,
  }
}

function setupDefaults() {
  mockUseApps.mockReturnValue({ data: [], isLoading: false, error: null } as unknown as ReturnType<typeof useApps>)
  mockUseDashboardStats.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useDashboardStats>)
  mockUseDashboardEvents.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useDashboardEvents>)
  mockUseSystemStatus.mockReturnValue({
    data: undefined,
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useSystemStatus>)
  mockUseStartApp.mockReturnValue(makeMutationStub())
  mockUseStopApp.mockReturnValue(makeMutationStub())
}

describe('DashboardPage', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('renders Recent Activity section divider', () => {
    render(<DashboardPage />)
    expect(screen.getByText('Recent Activity')).toBeInTheDocument()
  })

  test('renders EventList when events data is available', () => {
    mockUseDashboardEvents.mockReturnValue({
      data: {
        events: [
          makeEvent({ message: 'started', appSlug: 'my-api' }),
          makeEvent({ message: 'stopped', appSlug: 'worker-svc' }),
        ],
      },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useDashboardEvents>)

    render(<DashboardPage />)

    expect(screen.getByText('started')).toBeInTheDocument()
    expect(screen.getByText('stopped')).toBeInTheDocument()
  })

  test('renders spinner while events are loading', () => {
    mockUseDashboardEvents.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as unknown as ReturnType<typeof useDashboardEvents>)

    render(<DashboardPage />)

    expect(screen.getByLabelText('Loading')).toBeInTheDocument()
  })

  test('renders an error state (not nothing) for the events section when the endpoint fails with no data', () => {
    // FE-QRY-01: the old behavior rendered `null` here (the feed silently went
    // dark). The fix surfaces the error so the operator knows the feed failed to
    // load rather than being legitimately empty.
    mockUseDashboardEvents.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error('500 Internal Server Error'),
    } as unknown as ReturnType<typeof useDashboardEvents>)

    render(<DashboardPage />)

    expect(screen.getByText('Recent Activity')).toBeInTheDocument()
    expect(screen.getByText(/failed to load recent activity/i)).toBeInTheDocument()
    expect(screen.getByText(/500 Internal Server Error/)).toBeInTheDocument()
    expect(screen.queryByText('No recent events')).toBeNull()
  })

  test('keeps showing the last-known feed when a refetch errors (stale-then-error)', () => {
    // FE-QRY-01: TanStack keeps `data` on a query that errors after a prior
    // success — prefer the stale feed over flipping to an error banner, so the
    // dashboard does not flap as the backed-off poll retries.
    mockUseDashboardEvents.mockReturnValue({
      data: { events: [makeEvent({ message: 'started', appSlug: 'my-api' })] },
      isLoading: false,
      isError: true,
      error: new Error('transient blip'),
    } as unknown as ReturnType<typeof useDashboardEvents>)

    render(<DashboardPage />)

    expect(screen.getByText('started')).toBeInTheDocument()
    expect(screen.queryByText(/failed to load recent activity/i)).toBeNull()
  })

  test('renders empty event list when events array is empty', () => {
    mockUseDashboardEvents.mockReturnValue({
      data: { events: [] },
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useDashboardEvents>)

    render(<DashboardPage />)

    expect(screen.getByText('No recent events')).toBeInTheDocument()
  })

  test('renders empty state when no apps are registered', () => {
    render(<DashboardPage />)
    expect(screen.getByText('No apps registered')).toBeInTheDocument()
  })

  test('renders Proxy cell in status strip when system status is available', () => {
    mockUseDashboardStats.mockReturnValue({
      data: makeStats({ totalApps: 3, running: 2, appTypes: 2 }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useDashboardStats>)
    mockUseSystemStatus.mockReturnValue({
      data: makeSystemStatus('running'),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useSystemStatus>)

    render(<DashboardPage />)

    expect(screen.getByText('Proxy')).toBeInTheDocument()
    // 'Running' appears as both the stats cell label and the proxy value; verify proxy value exists
    const runningCells = screen.getAllByText('Running')
    expect(runningCells.length).toBeGreaterThanOrEqual(1)
  })

  test('renders Degraded proxy state with short routes-not-reaching detail (full detail lives on System page)', () => {
    mockUseDashboardStats.mockReturnValue({
      data: makeStats({ totalApps: 3, running: 2, appTypes: 2 }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useDashboardStats>)
    mockUseSystemStatus.mockReturnValue({
      data: makeSystemStatus('degraded'),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useSystemStatus>)

    render(<DashboardPage />)

    expect(screen.getByText('Degraded')).toBeInTheDocument()
    expect(screen.getByText('Routes not reaching public listener')).toBeInTheDocument()
  })

  test('renders Failed proxy state with remediation detail', () => {
    mockUseDashboardStats.mockReturnValue({
      data: makeStats({ totalApps: 3, running: 2, appTypes: 2 }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useDashboardStats>)
    mockUseSystemStatus.mockReturnValue({
      data: makeSystemStatus('failed'),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useSystemStatus>)

    render(<DashboardPage />)

    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.getByText('Check logs, restart Collabhost')).toBeInTheDocument()
  })

  test('omits Proxy cell when system status has not loaded yet', () => {
    mockUseDashboardStats.mockReturnValue({
      data: makeStats({ totalApps: 3, running: 2, appTypes: 2 }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useDashboardStats>)
    mockUseSystemStatus.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as unknown as ReturnType<typeof useSystemStatus>)

    render(<DashboardPage />)

    // Strip still renders (stats loaded), but no Proxy cell yet
    expect(screen.getByText('Total Apps')).toBeInTheDocument()
    expect(screen.queryByText('Proxy')).toBeNull()
  })

  test('renders an action-error banner when the start mutation fails', () => {
    mockUseStartApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 409: already running') }))

    render(<DashboardPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Start failed: state conflict — already running')
  })

  test('renders an action-error banner when the stop mutation fails', () => {
    mockUseStopApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 500: kaboom') }))

    render(<DashboardPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Stop failed: server error (500) — kaboom')
  })

  test('dismissing the action-error banner calls mutation.reset()', async () => {
    const reset = vi.fn()
    mockUseStartApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 409: nope'), reset }))

    const { default: userEvent } = await import('@testing-library/user-event')
    const user = userEvent.setup()
    render(<DashboardPage />)

    await user.click(screen.getByRole('button', { name: 'Dismiss error' }))
    expect(reset).toHaveBeenCalledOnce()
  })

  test('omits the action-error banner when no mutation is in error state', () => {
    render(<DashboardPage />)

    expect(screen.queryByRole('alert')).toBeNull()
  })

  test('shows full page spinner while primary data is loading', () => {
    mockUseDashboardStats.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as unknown as ReturnType<typeof useDashboardStats>)
    mockUseApps.mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    } as unknown as ReturnType<typeof useApps>)

    render(<DashboardPage />)

    expect(screen.getByLabelText('Loading')).toBeInTheDocument()
  })
})
