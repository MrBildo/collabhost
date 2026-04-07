import { ActionButton } from '@/actions/ActionButton'
import { ApiError } from '@/api/client'
import type { UserCreateResponse, UserRole } from '@/api/types'
import { useCreateUser } from '@/hooks/use-users'
import { ROUTES } from '@/lib/routes'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { KeyRevealDialog } from '@/shared/KeyRevealDialog'
import { PermissionGate } from '@/shared/PermissionGate'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'

function UserCreatePageContent() {
  const navigate = useNavigate()
  const createMutation = useCreateUser()

  const [name, setName] = useState('')
  const [role, setRole] = useState<UserRole>('agent')
  const [error, setError] = useState<string | null>(null)
  const [createdUser, setCreatedUser] = useState<UserCreateResponse | null>(null)

  function handleSubmit(e: React.FormEvent): void {
    e.preventDefault()
    const trimmedName = name.trim()
    if (!trimmedName) return

    setError(null)
    createMutation.mutate(
      { name: trimmedName, role },
      {
        onSuccess: (user) => {
          setCreatedUser(user)
        },
        onError: (err) => {
          if (err instanceof ApiError) {
            if (err.statusCode === 400) {
              setError(err.body || 'Invalid input. Please check the form and try again.')
            } else {
              setError('Failed to create user. Please try again.')
            }
          } else {
            setError('Failed to create user. Please try again.')
          }
        },
      },
    )
  }

  function handleDialogDone(): void {
    setCreatedUser(null)
    navigate(ROUTES.users)
  }

  const canSubmit = name.trim().length > 0 && !createMutation.isPending

  return (
    <div>
      <div
        className="flex items-baseline justify-between mb-5 pb-3"
        style={{ borderBottom: '1px solid var(--wm-border)' }}
      >
        <h1 className="wm-section-title" style={{ borderBottom: 'none', paddingBottom: 0 }}>
          <span style={{ color: 'var(--wm-text-dim)' }}>{'// '}</span>New User
        </h1>
      </div>

      <div style={{ maxWidth: 480 }}>
        <div className="wm-panel p-6">
          {error && <ErrorBanner message={error} className="mb-4" onDismiss={() => setError(null)} />}

          <form onSubmit={handleSubmit}>
            {/* Name field */}
            <div className="mb-5">
              <label
                htmlFor="user-name"
                className="block text-xs mb-1.5"
                style={{
                  color: 'var(--wm-text-dim)',
                  fontFamily: 'var(--wm-mono)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.06em',
                }}
              >
                Name
              </label>
              <input
                id="user-name"
                type="text"
                className="wm-input w-full"
                value={name}
                maxLength={100}
                placeholder="e.g., CI Agent, Deploy Bot, Jane"
                onChange={(e) => setName(e.target.value)}
                // biome-ignore lint/a11y/noAutofocus: first field on a single-purpose page
                autoFocus
              />
            </div>

            {/* Role selector */}
            <div className="mb-6">
              <fieldset>
                <legend
                  className="block text-xs mb-2"
                  style={{
                    color: 'var(--wm-text-dim)',
                    fontFamily: 'var(--wm-mono)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.06em',
                  }}
                >
                  Role
                </legend>

                <div className="flex flex-col gap-3">
                  <label
                    className="flex items-start gap-3 cursor-pointer p-3"
                    style={{
                      border: `1px solid ${role === 'agent' ? 'var(--wm-border-active)' : 'var(--wm-border)'}`,
                      borderRadius: 'var(--wm-radius)',
                      background: role === 'agent' ? 'var(--wm-bg-hover)' : 'transparent',
                    }}
                  >
                    <input
                      type="radio"
                      name="role"
                      value="agent"
                      checked={role === 'agent'}
                      onChange={() => setRole('agent')}
                      className="mt-0.5"
                    />
                    <div>
                      <div
                        style={{ fontFamily: 'var(--wm-mono)', fontSize: 'var(--wm-font-sm)', color: 'var(--wm-text)' }}
                      >
                        Agent
                      </div>
                      <div style={{ fontSize: 'var(--wm-font-xs)', color: 'var(--wm-text-dim)', marginTop: 2 }}>
                        Operational access. Can manage apps, settings, and routes.
                      </div>
                    </div>
                  </label>

                  <label
                    className="flex items-start gap-3 cursor-pointer p-3"
                    style={{
                      border: `1px solid ${role === 'administrator' ? 'var(--wm-amber-border)' : 'var(--wm-border)'}`,
                      borderRadius: 'var(--wm-radius)',
                      background: role === 'administrator' ? 'var(--wm-amber-dim)' : 'transparent',
                    }}
                  >
                    <input
                      type="radio"
                      name="role"
                      value="administrator"
                      checked={role === 'administrator'}
                      onChange={() => setRole('administrator')}
                      className="mt-0.5"
                    />
                    <div>
                      <div
                        style={{ fontFamily: 'var(--wm-mono)', fontSize: 'var(--wm-font-sm)', color: 'var(--wm-text)' }}
                      >
                        Administrator
                      </div>
                      <div style={{ fontSize: 'var(--wm-font-xs)', color: 'var(--wm-text-dim)', marginTop: 2 }}>
                        Full platform access. Can manage users and delete apps.
                      </div>
                    </div>
                  </label>
                </div>
              </fieldset>
            </div>

            {/* Actions */}
            <div className="flex items-center justify-between">
              <ActionButton variant="default" onClick={() => navigate(ROUTES.users)}>
                Cancel
              </ActionButton>
              <ActionButton variant="primary" type="submit" disabled={!canSubmit}>
                {createMutation.isPending ? 'Creating...' : 'Create User'}
              </ActionButton>
            </div>
          </form>
        </div>
      </div>

      {createdUser && <KeyRevealDialog user={createdUser} onDone={handleDialogDone} />}
    </div>
  )
}

function UserCreatePage() {
  return (
    <PermissionGate requiredRole="administrator">
      <UserCreatePageContent />
    </PermissionGate>
  )
}

export { UserCreatePage }
