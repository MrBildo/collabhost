import { AUTH_STORAGE_KEY } from '@/lib/constants'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, test } from 'vitest'
import { AuthGate } from './AuthGate'

function makeQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: Number.POSITIVE_INFINITY },
    },
  })
}

function renderWithClient(client: QueryClient) {
  return render(
    <QueryClientProvider client={client}>
      <AuthGate>
        <div data-testid="protected-content">protected content</div>
      </AuthGate>
    </QueryClientProvider>,
  )
}

describe('AuthGate', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
  })

  test('renders the login form when no key is stored', () => {
    const client = makeQueryClient()
    renderWithClient(client)

    expect(screen.getByLabelText('User Key')).toBeInTheDocument()
    expect(screen.queryByTestId('protected-content')).not.toBeInTheDocument()
  })

  test('renders children when a key is already stored', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01TESTKEY')
    const client = makeQueryClient()
    renderWithClient(client)

    expect(screen.getByTestId('protected-content')).toBeInTheDocument()
  })

  test('login submits trimmed key and renders children', async () => {
    const user = userEvent.setup()
    const client = makeQueryClient()
    renderWithClient(client)

    await user.type(screen.getByLabelText('User Key'), '  01ABC  ')
    await user.click(screen.getByRole('button', { name: /authenticate/i }))

    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBe('01ABC')
    expect(screen.getByTestId('protected-content')).toBeInTheDocument()
  })

  test('shows validation error for empty submit', async () => {
    const user = userEvent.setup()
    const client = makeQueryClient()
    renderWithClient(client)

    await user.click(screen.getByRole('button', { name: /authenticate/i }))

    expect(screen.getByText('User key is required')).toBeInTheDocument()
    expect(localStorage.getItem(AUTH_STORAGE_KEY)).toBeNull()
  })

  test('cache is cleared on logout-driven transition', async () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    const client = makeQueryClient()
    client.setQueryData(['auth', 'me'], { id: 'a', name: 'Alice' })
    client.setQueryData(['apps'], [{ id: 'app1' }])

    const { rerender } = render(
      <QueryClientProvider client={client}>
        <AuthGate>
          <div data-testid="protected-content">protected content</div>
        </AuthGate>
      </QueryClientProvider>,
    )

    expect(screen.getByTestId('protected-content')).toBeInTheDocument()
    expect(client.getQueryData(['auth', 'me'])).toBeDefined()

    // Use the hook's logout via the module emit path: import emitChange and
    // trigger after removing the storage key.
    const { emitChange } = await import('@/hooks/use-auth')
    act(() => {
      localStorage.removeItem(AUTH_STORAGE_KEY)
      emitChange()
    })

    rerender(
      <QueryClientProvider client={client}>
        <AuthGate>
          <div data-testid="protected-content">protected content</div>
        </AuthGate>
      </QueryClientProvider>,
    )

    // Login form is back, children unmounted, cache wiped.
    expect(screen.queryByTestId('protected-content')).not.toBeInTheDocument()
    expect(screen.getByLabelText('User Key')).toBeInTheDocument()
    expect(client.getQueryData(['auth', 'me'])).toBeUndefined()
    expect(client.getQueryData(['apps'])).toBeUndefined()
  })

  test('does not clear cache on initial mount when already authenticated', () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    const client = makeQueryClient()
    client.setQueryData(['apps'], [{ id: 'app1' }])

    renderWithClient(client)

    // Initial mount of an already-authenticated session must not wipe cache —
    // the cache may have been hydrated by parallel-fetched queries from a
    // suspense boundary or persisted state.
    expect(client.getQueryData(['apps'])).toBeDefined()
  })

  test('does not clear cache on initial mount when not authenticated', () => {
    const client = makeQueryClient()
    // Cache is empty; first render is the login form.
    renderWithClient(client)

    expect(screen.getByLabelText('User Key')).toBeInTheDocument()
    // No throw, no leftover state.
    expect(client.getQueryData(['anything'])).toBeUndefined()
  })

  test('cache is cleared on 401-driven transition', async () => {
    localStorage.setItem(AUTH_STORAGE_KEY, '01USER_A')
    const client = makeQueryClient()
    client.setQueryData(['auth', 'me'], { id: 'a', name: 'Alice' })

    const { rerender } = render(
      <QueryClientProvider client={client}>
        <AuthGate>
          <div data-testid="protected-content">protected content</div>
        </AuthGate>
      </QueryClientProvider>,
    )

    // Simulate the api/client.ts 401 path: localStorage cleared and emitChange
    // called from the fetch wrapper.
    const { emitChange } = await import('@/hooks/use-auth')
    act(() => {
      localStorage.removeItem(AUTH_STORAGE_KEY)
      emitChange()
    })

    rerender(
      <QueryClientProvider client={client}>
        <AuthGate>
          <div data-testid="protected-content">protected content</div>
        </AuthGate>
      </QueryClientProvider>,
    )

    expect(client.getQueryData(['auth', 'me'])).toBeUndefined()
  })
})
