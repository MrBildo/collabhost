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
    probesStatus: 'never-probed',
    probes: [],
    resources: null,
    route: null,
    actions: { canStart: false, canStop: true, canRestart: true, canKill: true },
    writableDataPath: '/var/lib/collabhost/data/app-data/my-api',
    tabs: ['logs', 'technology'],
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

  test('a kill failure does NOT surface in the top action banner (it lives in the dialog now, FE-UI-04)', () => {
    // Kill no longer flows through the top action-error banner — confirming a
    // kill that fails surfaces inside the confirm dialog. A bare killMutation
    // isError (no dialog open) must not paint the top banner.
    mockUseDetailKillApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 404: ') }))

    render(<AppDetailPage />)

    expect(screen.queryByRole('alert')).toBeNull()
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

describe('AppDetailPage tabs (Card #348 D5)', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('renders Logs + Technology tabs for a managed app (default backend shape)', () => {
    render(<AppDetailPage />)

    expect(screen.getByRole('tab', { name: 'Logs' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Technology' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Health' })).toBeNull()
    expect(screen.queryByRole('tab', { name: 'Route' })).toBeNull()
  })

  test('the active tab is marked aria-selected (FE-UI-06)', () => {
    render(<AppDetailPage />)

    // Default active tab is the first declared tab (logs).
    expect(screen.getByRole('tab', { name: 'Logs' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Technology' })).toHaveAttribute('aria-selected', 'false')
  })

  test('renders Health + Route tabs for an external-route app, hides Logs/Technology', () => {
    mockUseAppDetail.mockReturnValue({
      data: makeAppDetail({
        appType: { slug: 'external-route', displayName: 'External Route' },
        tabs: ['health', 'route'],
        pid: null,
        port: null,
        route: { domain: 'crawl4ai.collab.internal', target: 'http://192.168.1.50:11235', tls: true },
        healthStatus: 'healthy',
      }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useAppDetail>)

    render(<AppDetailPage />)

    expect(screen.getByRole('tab', { name: 'Health' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Route' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Logs' })).toBeNull()
    expect(screen.queryByRole('tab', { name: 'Technology' })).toBeNull()
  })

  test('renders external-route health label as "Reachable" (Card #348 D6 terminology split)', () => {
    mockUseAppDetail.mockReturnValue({
      data: makeAppDetail({
        appType: { slug: 'external-route', displayName: 'External Route' },
        tabs: ['health', 'route'],
        pid: null,
        port: null,
        healthStatus: 'healthy',
      }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useAppDetail>)

    render(<AppDetailPage />)

    // External-route's stats strip shrinks to Uptime + Health; the cell value
    // is the terminology-split label, not "Healthy".
    expect(screen.getAllByText('Reachable').length).toBeGreaterThan(0)
    expect(screen.queryByText('Healthy')).toBeNull()
  })

  test('opens the SSE log stream for a managed app with a logs tab (FE-XT-05)', () => {
    render(<AppDetailPage />)

    // Default app shape has tabs ['logs', 'technology'] → stream enabled.
    expect(mockUseLogStream).toHaveBeenCalledWith('my-api', expect.objectContaining({ enabled: true }))
  })

  test('does NOT open the SSE log stream for a routing-only app (no logs tab) (FE-XT-05)', () => {
    mockUseAppDetail.mockReturnValue({
      data: makeAppDetail({
        appType: { slug: 'external-route', displayName: 'External Route' },
        tabs: ['health', 'route'],
        pid: null,
        port: null,
      }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useAppDetail>)

    render(<AppDetailPage />)

    expect(mockUseLogStream).toHaveBeenCalledWith('my-api', expect.objectContaining({ enabled: false }))
  })

  test('falls back to [logs, technology] when backend tabs field is empty (defensive)', () => {
    mockUseAppDetail.mockReturnValue({
      data: makeAppDetail({ tabs: [] }),
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useAppDetail>)

    render(<AppDetailPage />)

    expect(screen.getByRole('tab', { name: 'Logs' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Technology' })).toBeInTheDocument()
  })
})

describe('AppDetailPage Kill confirm (FE-UI-04)', () => {
  beforeEach(() => {
    setupDefaults()
    // jsdom does not implement <dialog>.showModal / .close — polyfill so the
    // ConfirmDialog's open/close effect works (mirrors ConfirmDialog.test.tsx).
    HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
      this.setAttribute('open', '')
    })
    HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) {
      this.removeAttribute('open')
    })
  })

  test('clicking Kill does NOT immediately mutate — it opens a confirm dialog', async () => {
    const mutate = vi.fn()
    mockUseDetailKillApp.mockReturnValue(makeMutationStub({}))
    // Replace mutate with our spy.
    const stub = makeMutationStub({})
    ;(stub as unknown as { mutate: typeof mutate }).mutate = mutate
    mockUseDetailKillApp.mockReturnValue(stub)

    const { default: userEvent } = await import('@testing-library/user-event')
    const user = userEvent.setup()
    render(<AppDetailPage />)

    await user.click(screen.getByRole('button', { name: 'Kill' }))

    // The mutation has NOT fired — the confirm dialog is up instead.
    expect(mutate).not.toHaveBeenCalled()
    expect(screen.getByText('Kill App')).toBeInTheDocument()
    expect(screen.getByText(/Force-kill "My API"/)).toBeInTheDocument()
  })

  test('confirming the dialog fires the kill mutation', async () => {
    const mutate = vi.fn()
    const stub = makeMutationStub({})
    ;(stub as unknown as { mutate: typeof mutate }).mutate = mutate
    mockUseDetailKillApp.mockReturnValue(stub)

    const { default: userEvent } = await import('@testing-library/user-event')
    const user = userEvent.setup()
    render(<AppDetailPage />)

    await user.click(screen.getByRole('button', { name: 'Kill' }))
    // The dialog's confirm button is the second "Kill"-labelled control; scope
    // to the open dialog to avoid the action-bar Kill button.
    const dialog = document.querySelector('dialog')
    const confirmButton = dialog
      ? Array.from(dialog.querySelectorAll('button')).find((b) => b.textContent === 'Kill')
      : undefined
    expect(confirmButton).toBeDefined()
    if (confirmButton) await user.click(confirmButton)

    expect(mutate).toHaveBeenCalledWith('my-api', expect.any(Object))
  })
})
