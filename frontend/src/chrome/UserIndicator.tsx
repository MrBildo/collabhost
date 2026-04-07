import { useCurrentUser } from '@/hooks/use-current-user'
import { RoleBadge } from '@/status/RoleBadge'

function UserIndicator() {
  const { data: currentUser, isLoading } = useCurrentUser()

  if (isLoading) {
    return (
      <div className="wm-topbar__user" aria-label="Loading user identity" style={{ opacity: 0.4 }}>
        <span
          style={{
            display: 'inline-block',
            width: 48,
            height: 10,
            background: 'var(--wm-border)',
            borderRadius: 'var(--wm-radius-sm)',
          }}
        />
      </div>
    )
  }

  if (!currentUser) {
    return null
  }

  return (
    <div className="wm-topbar__user" aria-label={`Logged in as ${currentUser.name}`}>
      <RoleBadge role={currentUser.role} size="sm" />
      <span style={{ color: 'var(--wm-text-bright)', fontFamily: 'var(--wm-mono)', fontSize: 'var(--wm-font-xs)' }}>
        {currentUser.name}
      </span>
    </div>
  )
}

export { UserIndicator }
