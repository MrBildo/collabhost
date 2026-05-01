import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-apps', () => ({
  useApps: vi.fn(),
  useStartApp: vi.fn(),
  useStopApp: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { AppListPage } from './AppListPage'

const mockUseApps = vi.mocked(useApps)
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

function setupDefaults() {
  mockUseApps.mockReturnValue({ data: [], isLoading: false, error: null } as unknown as ReturnType<typeof useApps>)
  mockUseStartApp.mockReturnValue(makeMutationStub())
  mockUseStopApp.mockReturnValue(makeMutationStub())
}

describe('AppListPage', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('renders empty state when no apps are registered', () => {
    render(<AppListPage />)
    expect(screen.getByText('No apps registered')).toBeInTheDocument()
  })

  test('renders an action-error banner when start mutation fails', () => {
    mockUseStartApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 409: already running') }))

    render(<AppListPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Start failed: state conflict — already running')
  })

  test('renders an action-error banner when stop mutation fails', () => {
    mockUseStopApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 500: oops') }))

    render(<AppListPage />)

    expect(screen.getByRole('alert')).toHaveTextContent('Stop failed: server error (500) — oops')
  })

  test('dismissing the action-error banner calls mutation.reset()', async () => {
    const reset = vi.fn()
    mockUseStartApp.mockReturnValue(makeMutationStub({ isError: true, error: new Error('API 409: nope'), reset }))

    const { default: userEvent } = await import('@testing-library/user-event')
    const user = userEvent.setup()
    render(<AppListPage />)

    await user.click(screen.getByRole('button', { name: 'Dismiss error' }))
    expect(reset).toHaveBeenCalledOnce()
  })

  test('omits the action-error banner when no mutation is in error state', () => {
    render(<AppListPage />)

    expect(screen.queryByRole('alert')).toBeNull()
  })
})
