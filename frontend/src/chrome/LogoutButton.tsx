import { ActionButton } from '@/actions/ActionButton'
import { useAuth } from '@/hooks/use-auth'
import { useCurrentUser } from '@/hooks/use-current-user'

function LogoutButton() {
  const { isAuthenticated, logout } = useAuth()
  const { data: currentUser, isLoading } = useCurrentUser()

  // Hide while we don't yet know who's logged in — the UserIndicator carries
  // the loading skeleton; the button shouldn't render until the identity it
  // signs out is known.
  if (!isAuthenticated || isLoading || !currentUser) {
    return null
  }

  return (
    <ActionButton variant="ghost" size="sm" onClick={logout}>
      Sign out
    </ActionButton>
  )
}

export { LogoutButton }
