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
  useCreateUser: vi.fn(),
}))

vi.mock('@/hooks/use-current-user', () => ({
  useCurrentUser: vi.fn(),
}))

vi.mock('react-router-dom', () => ({
  useNavigate: () => vi.fn(),
}))

import { useCurrentUser } from '@/hooks/use-current-user'
import { useCreateUser } from '@/hooks/use-users'
import { UserCreatePage } from './UserCreatePage'

const mockUseCreateUser = vi.mocked(useCreateUser)
const mockUseCurrentUser = vi.mocked(useCurrentUser)

function setupAdminUser() {
  mockUseCurrentUser.mockReturnValue({
    data: { id: 'admin-1', name: 'Admin', role: 'administrator' as const, isActive: true, createdAt: '2026-01-01' },
    isLoading: false,
  } as ReturnType<typeof useCurrentUser>)
}

function makeCreatedUser() {
  return {
    id: 'new-user-1',
    name: 'Deploy Bot',
    role: 'agent' as const,
    isActive: true,
    createdAt: '2026-04-06T00:00:00Z',
    authKey: '01JRNTEST00000000000000000',
  }
}

describe('UserCreatePage', () => {
  beforeEach(() => {
    setupAdminUser()
    mockUseCreateUser.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
    } as unknown as ReturnType<typeof useCreateUser>)
  })

  test('renders form fields', () => {
    render(<UserCreatePage />)

    expect(screen.getByLabelText(/Name/i)).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Agent/i })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Administrator/i })).toBeInTheDocument()
  })

  test('Create User button is disabled when name is empty', () => {
    render(<UserCreatePage />)

    expect(screen.getByRole('button', { name: /Create User/i })).toBeDisabled()
  })

  test('Create User button enables after name is entered', async () => {
    const user = userEvent.setup()
    render(<UserCreatePage />)

    await user.type(screen.getByLabelText(/Name/i), 'CI Agent')

    expect(screen.getByRole('button', { name: /Create User/i })).not.toBeDisabled()
  })

  test('defaults to Agent role', () => {
    render(<UserCreatePage />)

    const agentRadio = screen.getByRole('radio', { name: /Agent/i })
    expect(agentRadio).toBeChecked()
  })

  test('can select Administrator role', async () => {
    const user = userEvent.setup()
    render(<UserCreatePage />)

    await user.click(screen.getByRole('radio', { name: /Administrator/i }))

    expect(screen.getByRole('radio', { name: /Administrator/i })).toBeChecked()
    expect(screen.getByRole('radio', { name: /Agent/i })).not.toBeChecked()
  })

  test('calls createUser with name and role on submit', async () => {
    const user = userEvent.setup()
    const mutate = vi.fn()
    mockUseCreateUser.mockReturnValue({
      mutate,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateUser>)

    render(<UserCreatePage />)

    await user.type(screen.getByLabelText(/Name/i), 'Deploy Bot')
    await user.click(screen.getByRole('button', { name: /Create User/i }))

    expect(mutate).toHaveBeenCalledWith(
      { name: 'Deploy Bot', role: 'agent' },
      expect.objectContaining({ onSuccess: expect.any(Function), onError: expect.any(Function) }),
    )
  })

  test('shows key reveal dialog on successful creation', async () => {
    const user = userEvent.setup()
    const mutate = vi.fn().mockImplementation((_body, options) => {
      options.onSuccess(makeCreatedUser())
    })
    mockUseCreateUser.mockReturnValue({
      mutate,
      isPending: false,
    } as unknown as ReturnType<typeof useCreateUser>)

    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      writable: true,
      configurable: true,
    })

    render(<UserCreatePage />)

    await user.type(screen.getByLabelText(/Name/i), 'Deploy Bot')
    await user.click(screen.getByRole('button', { name: /Create User/i }))

    expect(screen.getByText('User Created Successfully')).toBeInTheDocument()
    expect(screen.getByText('01JRNTEST00000000000000000')).toBeInTheDocument()
  })

  test('shows access denied for agent users', () => {
    mockUseCurrentUser.mockReturnValue({
      data: { id: 'a1', name: 'Bot', role: 'agent' as const, isActive: true, createdAt: '2026-01-01' },
      isLoading: false,
    } as ReturnType<typeof useCurrentUser>)

    render(<UserCreatePage />)

    expect(screen.getByText('Access Denied')).toBeInTheDocument()
  })
})
