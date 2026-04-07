import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, test, vi } from 'vitest'

// jsdom polyfill for dialog
beforeEach(() => {
  if (!HTMLDialogElement.prototype.showModal) {
    HTMLDialogElement.prototype.showModal = function () {
      this.setAttribute('open', '')
    }
  }
  if (!HTMLDialogElement.prototype.close) {
    HTMLDialogElement.prototype.close = function () {
      this.removeAttribute('open')
    }
  }
})

vi.mock('@/hooks/use-users', () => ({
  useUsers: vi.fn(),
  useDeactivateUser: vi.fn(),
}))

vi.mock('@/hooks/use-current-user', () => ({
  useCurrentUser: vi.fn(),
}))

vi.mock('@/hooks/use-auth', () => ({
  useAuth: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

import { useAuth } from '@/hooks/use-auth'
import { useCurrentUser } from '@/hooks/use-current-user'
import { useDeactivateUser, useUsers } from '@/hooks/use-users'
import { UsersPage } from './UsersPage'

const mockUseUsers = vi.mocked(useUsers)
const mockUseCurrentUser = vi.mocked(useCurrentUser)
const mockUseDeactivateUser = vi.mocked(useDeactivateUser)
const mockUseAuth = vi.mocked(useAuth)

function makeAdminUser(overrides = {}) {
  return {
    id: 'admin-1',
    name: 'Admin',
    role: 'administrator' as const,
    isActive: true,
    createdAt: '2026-04-01T00:00:00Z',
    ...overrides,
  }
}

function makeAgentUser(overrides = {}) {
  return {
    id: 'agent-1',
    name: 'CI Agent',
    role: 'agent' as const,
    isActive: true,
    createdAt: '2026-04-02T00:00:00Z',
    ...overrides,
  }
}

function setupDefaults() {
  mockUseCurrentUser.mockReturnValue({
    data: makeAdminUser(),
    isLoading: false,
  } as ReturnType<typeof useCurrentUser>)

  mockUseDeactivateUser.mockReturnValue({
    mutate: vi.fn(),
    isPending: false,
  } as unknown as ReturnType<typeof useDeactivateUser>)

  mockUseAuth.mockReturnValue({
    userKey: 'admin-key',
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
  })
}

describe('UsersPage', () => {
  beforeEach(() => {
    setupDefaults()
  })

  test('renders users table when users are loaded', () => {
    mockUseUsers.mockReturnValue({
      data: [makeAdminUser(), makeAgentUser()],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    expect(screen.getByText('Admin')).toBeInTheDocument()
    expect(screen.getByText('CI Agent')).toBeInTheDocument()
  })

  test('shows empty state when no users exist', () => {
    mockUseUsers.mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    expect(screen.getByText('No users found')).toBeInTheDocument()
  })

  test('renders role badges for each user', () => {
    mockUseUsers.mockReturnValue({
      data: [makeAdminUser(), makeAgentUser()],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    expect(screen.getByText('Administrator')).toBeInTheDocument()
    expect(screen.getAllByText('Agent')).toHaveLength(1)
  })

  test('shows Active status for active users', () => {
    mockUseUsers.mockReturnValue({
      data: [makeAgentUser({ isActive: true })],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  test('shows Inactive status for deactivated users', () => {
    mockUseUsers.mockReturnValue({
      data: [makeAgentUser({ isActive: false })],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    expect(screen.getByText('Inactive')).toBeInTheDocument()
  })

  test('inactive row has inactive class', () => {
    mockUseUsers.mockReturnValue({
      data: [makeAgentUser({ id: 'inactive-1', isActive: false })],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    const rows = document.querySelectorAll('tbody tr')
    expect(rows[0]).toHaveClass('wm-table-row--inactive')
  })

  test('deactivate button opens confirm dialog', async () => {
    const user = userEvent.setup()
    mockUseUsers.mockReturnValue({
      data: [makeAgentUser()],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    await user.click(screen.getByRole('button', { name: /Deactivate/i }))

    expect(screen.getByText(/Deactivate "CI Agent"/i)).toBeInTheDocument()
  })

  test('shows Access Denied for agent users', () => {
    mockUseCurrentUser.mockReturnValue({
      data: makeAgentUser(),
      isLoading: false,
    } as ReturnType<typeof useCurrentUser>)

    mockUseUsers.mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useUsers>)

    render(<UsersPage />)

    expect(screen.getByText('Access Denied')).toBeInTheDocument()
  })
})
