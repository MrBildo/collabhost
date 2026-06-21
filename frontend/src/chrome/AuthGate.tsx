import { ActionButton } from '@/actions/ActionButton'
import { fetchMe } from '@/api/endpoints'
import { LogoMark } from '@/chrome/LogoMark'
import { useAuth } from '@/hooks/use-auth'
import { AUTH_STORAGE_KEY } from '@/lib/constants'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'

type AuthGateProps = {
  children: ReactNode
}

function AuthGate({ children }: AuthGateProps) {
  const { isAuthenticated, login } = useAuth()
  const queryClient = useQueryClient()
  const [key, setKey] = useState('')
  const [error, setError] = useState('')
  const [isSubmitting, setIsSubmitting] = useState(false)
  const wasAuthenticatedRef = useRef(isAuthenticated)

  // Clear server-state cache when auth transitions from authenticated to
  // unauthenticated. Covers both explicit logout and 401-driven session expiry
  // — neither path should leave the previous user's cached data in memory for
  // the next session to read. Per-app slug-keyed children (AppDetailPage) also
  // need the SSE log-stream connection to die; it's tied to the AuthGate
  // children subtree, which is unmounted by the `if (!isAuthenticated)` branch
  // below.
  useEffect(() => {
    if (wasAuthenticatedRef.current && !isAuthenticated) {
      queryClient.clear()
    }
    wasAuthenticatedRef.current = isAuthenticated
  }, [isAuthenticated, queryClient])

  // Mount the protected children only once the session is COMMITTED — not while
  // a validation probe is still in flight. `isAuthenticated` derives from
  // `useSyncExternalStore(getSnapshot = localStorage)`, which React re-reads on
  // every render, so the moment `handleSubmit` writes the key to localStorage
  // (which the client wrapper needs on the probe) and schedules a render via
  // `setIsSubmitting(true)`, `isAuthenticated` flips true — before `fetchMe`
  // resolves. Gating on `!isSubmitting` keeps the children out of the DOM for
  // that in-flight window, so a bad/expired key never flashes the protected UI
  // before bouncing back to the gate (FE-AUTH-01, no-flash half).
  if (isAuthenticated && !isSubmitting) {
    return <>{children}</>
  }

  // Validate the key against `/auth/me` BEFORE committing the session, so a
  // wrong or expired key surfaces a message here instead of silently bouncing
  // off the gate (FE-AUTH-01). The previous code called `login()` immediately —
  // that flipped `isAuthenticated`, mounted the children, let the first API call
  // 401, and the client wrapper then cleared the key and re-rendered the gate
  // with no message: a flash, then back to a blank form.
  //
  // We write the key directly to localStorage (so the client wrapper attaches it
  // to the probe), then hold the gate open via `isSubmitting` while the probe
  // runs (see the committed-session guard above). On success we `login()` to
  // commit (which emits and renders the children). On a 401 the client wrapper
  // already cleared the key + emitted, so we must not double-handle it — we only
  // render the message. On any other failure we clear the key we wrote ourselves
  // so no orphaned key lingers for the next mount.
  async function handleSubmit(e: React.FormEvent): Promise<void> {
    e.preventDefault()
    const trimmed = key.trim()
    if (!trimmed) {
      setError('User key is required')
      return
    }

    setIsSubmitting(true)
    setError('')
    localStorage.setItem(AUTH_STORAGE_KEY, trimmed)
    try {
      await fetchMe()
      login(trimmed)
    } catch {
      // 401 → the client wrapper already removed the key and emitted; any other
      // error → remove the key we just wrote so it can't auto-authenticate the
      // next mount. removeItem is idempotent, so the 401 path is safe to repeat.
      localStorage.removeItem(AUTH_STORAGE_KEY)
      setError('Invalid or expired user key. Check the key and try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex items-center justify-center min-h-screen" style={{ background: 'var(--wm-bg)' }}>
      <div className="wm-auth-gate">
        <div className="mb-6">
          <div className="flex items-center gap-3 mb-2">
            <LogoMark size={40} />
            <span
              style={{
                fontFamily: 'var(--wm-mono)',
                fontSize: '22px',
                fontWeight: 500,
                letterSpacing: '0.04em',
              }}
            >
              <span style={{ color: 'var(--wm-text-dim)' }}>collab</span>
              <span style={{ color: 'var(--wm-amber)' }}>host</span>
            </span>
          </div>
          <p className="text-xs mt-2" style={{ color: 'var(--wm-text-dim)' }}>
            Enter your user key to continue.
          </p>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="mb-4">
            <label htmlFor="user-key" className="block text-xs mb-1.5" style={{ color: 'var(--wm-text-dim)' }}>
              User Key
            </label>
            <input
              id="user-key"
              type="password"
              className="wm-input w-full"
              value={key}
              disabled={isSubmitting}
              onChange={(e) => {
                setKey(e.target.value)
                setError('')
              }}
              placeholder="01JQRS..."
              // biome-ignore lint/a11y/noAutofocus: full-page auth gate, focus is the primary interaction
              autoFocus
            />
            {error && (
              <p className="text-xs mt-1" style={{ color: 'var(--wm-red)' }} role="alert">
                {error}
              </p>
            )}
          </div>
          <ActionButton
            variant="primary"
            size="lg"
            type="submit"
            disabled={isSubmitting}
            className="w-full justify-center"
          >
            {isSubmitting ? 'Authenticating…' : 'Authenticate'}
          </ActionButton>
        </form>
      </div>
    </div>
  )
}

export { AuthGate }
export type { AuthGateProps }
