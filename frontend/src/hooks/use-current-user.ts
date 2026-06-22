import { ApiError } from '@/api/client'
import { fetchMe } from '@/api/endpoints'
import { useAuth } from '@/hooks/use-auth'
import { useQuery } from '@tanstack/react-query'

// Identity is the spine of the chrome — sign-out, the Users nav, and every
// PermissionGate read from it. A single failed `/auth/me` must not freeze all
// of that (FE-AUTH-02). Retry transient failures a couple of times and refetch
// on reconnect so a blip self-heals; do NOT retry an auth/permission failure
// (401/403) — a 401 already clears the session via the client wrapper, and a
// 403 is a settled answer, not a blip.
const IDENTITY_RETRY_LIMIT = 2

function shouldRetryIdentity(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && (error.statusCode === 401 || error.statusCode === 403)) {
    return false
  }
  return failureCount < IDENTITY_RETRY_LIMIT
}

function useCurrentUser() {
  const { isAuthenticated } = useAuth()

  return useQuery({
    queryKey: ['auth', 'me'],
    queryFn: fetchMe,
    enabled: isAuthenticated,
    staleTime: Number.POSITIVE_INFINITY,
    retry: shouldRetryIdentity,
    refetchOnReconnect: true,
  })
}

export { useCurrentUser, shouldRetryIdentity }
