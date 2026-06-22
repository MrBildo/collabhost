import { ApiError } from '@/api/client'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, test, vi } from 'vitest'
import { PermissionGate } from './PermissionGate'

// Mock useCurrentUser and useNavigate
vi.mock('@/hooks/use-current-user', () => ({
  useCurrentUser: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

import { useCurrentUser } from '@/hooks/use-current-user'

vi.mock('@/shared/Spinner', () => ({
  Spinner: () => <div data-testid="spinner" />,
}))

const mockUseCurrentUser = vi.mocked(useCurrentUser)

describe('PermissionGate', () => {
  test('shows spinner while loading', () => {
    mockUseCurrentUser.mockReturnValue({ data: undefined, isLoading: true } as ReturnType<typeof useCurrentUser>)

    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    expect(screen.getByTestId('spinner')).toBeInTheDocument()
    expect(screen.queryByText('Admin content')).not.toBeInTheDocument()
  })

  test('renders children for administrator when required role is administrator', () => {
    mockUseCurrentUser.mockReturnValue({
      data: { id: 'u1', name: 'Alice', role: 'administrator', isActive: true, createdAt: '2026-01-01' },
      isLoading: false,
    } as ReturnType<typeof useCurrentUser>)

    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    expect(screen.getByText('Admin content')).toBeInTheDocument()
  })

  test('shows permission denied for agent when required role is administrator', () => {
    mockUseCurrentUser.mockReturnValue({
      data: { id: 'u1', name: 'Bot', role: 'agent', isActive: true, createdAt: '2026-01-01' },
      isLoading: false,
    } as ReturnType<typeof useCurrentUser>)

    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    expect(screen.queryByText('Admin content')).not.toBeInTheDocument()
    expect(screen.getByText('Access Denied')).toBeInTheDocument()
  })

  test('shows permission denied when user data is unavailable', () => {
    mockUseCurrentUser.mockReturnValue({
      data: undefined,
      isLoading: false,
    } as ReturnType<typeof useCurrentUser>)

    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    expect(screen.getByText('Access Denied')).toBeInTheDocument()
  })

  test('shows a retryable error (not Access Denied) on a transient identity failure', () => {
    // FE-AUTH-02: a failed /auth/me (network blip / 5xx) means we could not
    // verify the role — must read as a retryable error, not a permission denial.
    mockUseCurrentUser.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new ApiError(500, 'boom'),
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useCurrentUser>)

    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    expect(screen.getByText("Couldn't verify permissions")).toBeInTheDocument()
    expect(screen.queryByText('Access Denied')).not.toBeInTheDocument()
    expect(screen.queryByText('Admin content')).not.toBeInTheDocument()
  })

  test('shows Access Denied (not the transient retry) on a forbidden 403', () => {
    // FE-AUTH-02: a settled 403 is a genuine denial, distinct from a blip.
    mockUseCurrentUser.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new ApiError(403, 'forbidden'),
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useCurrentUser>)

    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    expect(screen.getByText('Access Denied')).toBeInTheDocument()
    expect(screen.queryByText("Couldn't verify permissions")).not.toBeInTheDocument()
  })

  test('the Retry button on a transient error calls refetch', async () => {
    const refetch = vi.fn()
    mockUseCurrentUser.mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new ApiError(500, 'boom'),
      refetch,
    } as unknown as ReturnType<typeof useCurrentUser>)

    const user = userEvent.setup()
    render(
      <PermissionGate requiredRole="administrator">
        <div>Admin content</div>
      </PermissionGate>,
    )

    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(refetch).toHaveBeenCalledOnce()
  })
})
