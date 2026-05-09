import { AUTH_STORAGE_KEY } from '@/lib/constants'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'

vi.mock('@/hooks/use-current-user', () => ({
  useCurrentUser: vi.fn(),
}))

import { useCurrentUser } from '@/hooks/use-current-user'
import { LogoutButton } from './LogoutButton'

const mockUseCurrentUser = vi.mocked(useCurrentUser)

function adminUser() {
  return {
    data: { id: 'u1', name: 'Alice', role: 'administrator', isActive: true, createdAt: '2026-01-01' },
    isLoading: false,
  } as ReturnType<typeof useCurrentUser>
}

describe('LogoutButton', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
  })

  test('renders nothing when not authenticated', () => {
    mockUseCurrentUser.mockReturnValue(adminUser())

    const { container } = render(<LogoutButton />)
    expect(container).toBeEmptyDOMElement()
  })

  test('renders nothing while current-user is loading', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    mockUseCurrentUser.mockReturnValue({ data: undefined, isLoading: true } as ReturnType<typeof useCurrentUser>)

    const { container } = render(<LogoutButton />)
    expect(container).toBeEmptyDOMElement()
  })

  test('renders nothing when current-user resolves with no data', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    mockUseCurrentUser.mockReturnValue({ data: undefined, isLoading: false } as ReturnType<typeof useCurrentUser>)

    const { container } = render(<LogoutButton />)
    expect(container).toBeEmptyDOMElement()
  })

  test('renders Sign out button when authenticated', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    mockUseCurrentUser.mockReturnValue(adminUser())

    render(<LogoutButton />)
    expect(screen.getByRole('button', { name: /sign out/i })).toBeInTheDocument()
  })

  test('clicking Sign out clears the auth key from localStorage', async () => {
    const user = userEvent.setup()
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    mockUseCurrentUser.mockReturnValue(adminUser())

    render(<LogoutButton />)
    await user.click(screen.getByRole('button', { name: /sign out/i }))

    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })
})
