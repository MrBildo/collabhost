import { render, screen } from '@testing-library/react'
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
})
