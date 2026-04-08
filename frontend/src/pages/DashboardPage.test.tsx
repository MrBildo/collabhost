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

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}))

import type { DashboardEvent } from '@/api/types'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { useDashboardEvents, useDashboardStats } from '@/hooks/use-dashboard'
import { DashboardPage } from './DashboardPage'

const mockUseApps = vi.mocked(useApps)
const mockUseDashboardStats = vi.mocked(useDashboardStats)
const mockUseDashboardEvents = vi.mocked(useDashboardEvents)
const mockUseStartApp = vi.mocked(useStartApp)
const mockUseStopApp = vi.mocked(useStopApp)

function makeMutationStub() {
  return { mutate: vi.fn(), isPending: false } as unknown as ReturnType<typeof useStartApp>
}

function makeEvent(overrides: Partial<DashboardEvent> = {}): DashboardEvent {
  return {
    timestamp: '2026-04-07T12:00:00Z',
    message: 'started',
    appSlug: 'my-api',
    source: 'Admin',
    severity: 'info',
    ...overrides,
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

  test('renders nothing for events section when events endpoint fails', () => {
    mockUseDashboardEvents.mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('500 Internal Server Error'),
    } as unknown as ReturnType<typeof useDashboardEvents>)

    render(<DashboardPage />)

    // No error banner for events — graceful degradation
    // The section divider still renders, but no event list or spinner
    expect(screen.getByText('Recent Activity')).toBeInTheDocument()
    expect(screen.queryByText('No recent events')).toBeNull()
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
