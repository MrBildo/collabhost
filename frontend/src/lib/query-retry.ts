import { ApiError } from '@/api/client'

// Default TanStack-Query retry policy (FE-QRY-04). The global `retry: 1` retried
// every failure once — including 4xx client errors (404, 400, 403) that are
// settled answers, not transient blips. Retrying them wastes a round-trip and,
// under the log surfaces, doubles the noise. A 5xx / network failure is the only
// thing worth a second attempt.
//
// `failureCount` is 0-based and fires BEFORE the increment (query-core retryer):
// the first failure calls back with 0. A limit of 1 therefore yields one retry
// (two attempts total) for retryable errors.
const DEFAULT_RETRY_LIMIT = 1

function isRetryableError(error: unknown): boolean {
  // A non-ApiError (network failure, parse error) has no status — treat as
  // transient and retryable. An ApiError with a 4xx status is a client-side
  // settled answer and must not be retried; 5xx is server-side and may recover.
  if (error instanceof ApiError) {
    return error.statusCode >= 500
  }
  return true
}

function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (!isRetryableError(error)) {
    return false
  }
  return failureCount < DEFAULT_RETRY_LIMIT
}

export { shouldRetryQuery, isRetryableError }
