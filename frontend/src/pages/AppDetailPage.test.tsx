import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-app-detail', () => ({
  useAppDetail: vi.fn(),
  useAppLogs: vi.fn(),
  useDetailStartApp: vi.fn(),
  useDetailStopApp: vi.fn(),
  useDetailRestartApp: vi.fn(),
  useDetailKillApp: vi.fn(),
}))

vi.mock('@/hooks/use-log-stream', () => ({
  useLogStream: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useParams: () => ({ slug: 'my-api' }),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}))

import type { AppDetail } from '@/api/types'
import {
  useAppDetail,
  useAppLogs,
  useDetailKillApp,
  useDetailRestartApp,
  useDetailStartApp,
  useDetailStopApp,
} from '@/hooks/use-app-detail'
import { useLogStream } from '@/hooks/use-log-stream'
import { AppDetailPage } from './AppDetailPage'

const mockUseAppDetail = vi.mocked(useAppDetail)
const mockUseAppLogs = vi.mocked(useAppLogs)
const mockUseLogStream = vi.mocked(useLogStream)
const mockUseDetailStartApp = vi.mocked(useDetailStartApp)
const mockUseDetailStopApp = vi.mocked(useDetailStopApp)
const mockUseDetailRestartApp = vi.mocked(useDetailRestartApp)
const mockUseDetailKillApp = vi.mocked(useDetailKillApp)

function makeMutationStub(
  overrides: Partial<{ isError: boolean; error: Error | null; isPending: boolean; reset: () => void }> = {},
) {
  return {
    mutate: vi.fn(),
    isPending: overrides.isPending ?? false,
    isError: overrides.isError ?? false,
    error: overrides.error ?? null,
    reset: overrides.reset ?? vi.fn(),
  } as unknown as ReturnType<typeof useDetailStartApp>
}

function makeAppDetail(overrides: Partial<AppDetail> = {}): AppDetail {
  return {
    id: '01ULID0000000000000000000A',
    name: 'my-api',
    displayName: 'My API',
    appType: { slug: 'dotnet-app', displayName: '.NET App' },
    registeredAt: '2026-04-01T00:00:00Z',
    status: 'running',
    pid: 1234,
    port: 5100,
    uptimeSeconds: 60,
    restartCount: 0,
    restartPolicy: null,
    autoStart: null,
    domain: null,
    domainActive: false,
    healthStatus: null,
    probes: [],
    resources: null,
    route: null,
    actions: { canStart: false, canStop: true, canRestart: true, canKill: true, canUpdate: false },
    ...overrides,
  }
}

function setupDefaults() {
  mockUseAppDetail.mockReturnValue({
    data: makeAppDetail(),
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useAppDetail>)

  mockUseAppLogs.mockReturnValue({
    data: { entries: [], totalBuffered: 0 },
    isLoading: false,
    error: null,
  } as unknown as ReturnType<typeof useAppLogs>)

  mockUseLogStream.mockReturnValue({
    entries: [],
    isStreaming: false,
    isConnected: false,
    error: null,
  })

  mockUseDetailStartApp.mockReturnValue(makeMutationStub())
  mockUseDetailStopApp.mockReturnValue(makeMutationStub())
  mockUseDetailRestartApp.mockReturnValue(makeMutationStub())
  mockUseDetailKillApp.mockReturnValue(makeMutationStub())
}

describe('AppDetailPage action-error banner', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('renders banner when start mutation fails', () => {
    mockUseDetailStartApp.mockReturnValue(
      makeMutationStub({ isError: true, error: new Error('API 409: already running') }),
    )

    render(<AppDetailPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Start failed: state conflict — already running')
  })

  test('renders banner when stop mutation fails', () => {
    mockUseDetailStopApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 500: kaboom') }))

    render(<AppDetailPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Stop failed: server error (500) — kaboom')
  })

  test('renders banner when restart mutation fails', () => {
    mockUseDetailRestartApp.mockReturnValue(
      makeMutationStub({ isError: true, error: new Error('API 409: not running') }),
    )

    render(<AppDetailPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Restart failed: state conflict — not running')
  })

  test('renders banner when kill mutation fails', () => {
    mockUseDetailKillApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 404: ') }))

    render(<AppDetailPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Kill failed: app not found')
  })

  test('dismissing the banner calls mutation.reset()', async () => {
    const reset = vi.fn()
    mockUseDetailStartApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 409: nope'), reset }))

    const { default: userEvent } = await import('@testing-library/user-event')
    const user = userEvent.setup()
    render(<AppDetailPage />)

    await user.click(screen.getByRole('button', { name: 'Dismiss error' }))
    expect(reset).toHaveBeenCalledOnce()
  })

  test('first error wins when multiple mutations are in error state', () => {
    // Order of precedence: start > stop > restart > kill
    mockUseDetailStartApp.mockReturnValue(
      makeMutationStub({ isError: true, error: new Error('API 409: start-conflict') }),
    )
    mockUseDetailStopApp.mockReturnValue(
      makeMutationStub({ isError: true, error: new Error('API 409: stop-conflict') }),
    )

    render(<AppDetailPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Start failed')
    expect(screen.getByRole('alert')).not.toHaveTextContent('Stop failed')
  })

  test('omits banner when no mutation is in error state', () => {
    render(<AppDetailPage />)

    expect(screen.queryByRole('alert')).toBeNull()
  })
})
