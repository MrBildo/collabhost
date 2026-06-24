import { fetchMe } from '@/api/endpoints'
import { useAuth } from '@/hooks/use-auth'
import { isRetryableError } from '@/lib/query-retry'
import { useQuery } from '@tanstack/react-query'

// Identity is the spine of the chrome — sign-out, the Users nav, and every
// PermissionGate read from it. A single failed `/auth/me` must not freeze all
// of that (FE-AUTH-02). Retry transient failures a couple of times and refetch
// on reconnect so a blip self-heals; do NOT retry an auth/permission failure
// (4xx) — a 401 already clears the session via the client wrapper, and a 403 is
// a settled answer, not a blip. `isRetryableError` (FE-QRY-04 shared policy)
// rejects every 4xx; identity allows a higher retry count for the transient
// 5xx / network case because the whole chrome depends on it.
const IDENTITY_RETRY_LIMIT = 2

function shouldRetryIdentity(failureCount: number, error: unknown): boolean {
  if (!isRetryableError(error)) {
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
