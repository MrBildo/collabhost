import { ActionButton } from '@/actions/ActionButton'
import { LogoMark } from '@/chrome/LogoMark'
import { useAuth } from '@/hooks/use-auth'
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

  if (isAuthenticated) {
    return <>{children}</>
  }

  function handleSubmit(e: React.FormEvent): void {
    e.preventDefault()
    const trimmed = key.trim()
    if (!trimmed) {
      setError('User key is required')
      return
    }
    login(trimmed)
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
              onChange={(e) => {
                setKey(e.target.value)
                setError('')
              }}
              placeholder="01JQRS..."
              // biome-ignore lint/a11y/noAutofocus: full-page auth gate, focus is the primary interaction
              autoFocus
            />
            {error && (
              <p className="text-xs mt-1" style={{ color: 'var(--wm-red)' }}>
                {error}
              </p>
            )}
          </div>
          <ActionButton variant="primary" size="lg" type="submit" className="w-full justify-center">
            Authenticate
          </ActionButton>
        </form>
      </div>
    </div>
  )
}

export { AuthGate }
export type { AuthGateProps }
