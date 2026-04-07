import { ActionButton } from '@/actions/ActionButton'
import { useAuth } from '@/hooks/use-auth'
import { useState } from 'react'
import type { ReactNode } from 'react'

type AuthGateProps = {
  children: ReactNode
}

function AuthGate({ children }: AuthGateProps) {
  const { isAuthenticated, login } = useAuth()
  const [key, setKey] = useState('')
  const [error, setError] = useState('')

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
          <h1 className="wm-section-title" style={{ borderBottom: 'none', paddingBottom: 0 }}>
            Collabhost
          </h1>
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
