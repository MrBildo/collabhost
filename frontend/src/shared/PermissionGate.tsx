import { ActionButton } from '@/actions/ActionButton'
import type { UserRole } from '@/api/types'
import { useCurrentUser } from '@/hooks/use-current-user'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { Spinner } from '@/shared/Spinner'
import type { ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'

type PermissionGateProps = {
  requiredRole: UserRole
  children: ReactNode
}

function PermissionGate({ requiredRole, children }: PermissionGateProps) {
  const navigate = useNavigate()
  const { data: currentUser, isLoading } = useCurrentUser()

  if (isLoading) {
    return (
      <div className="py-8">
        <Spinner />
      </div>
    )
  }

  if (!currentUser || currentUser.role !== requiredRole) {
    return (
      <EmptyState
        title="Access Denied"
        description={`You do not have permission to view this page. User management requires the ${requiredRole === 'administrator' ? 'Administrator' : 'Agent'} role.`}
        action={
          <ActionButton variant="default" onClick={() => navigate(ROUTES.dashboard)}>
            Go to Dashboard
          </ActionButton>
        }
      />
    )
  }

  return <>{children}</>
}

export { PermissionGate }
export type { PermissionGateProps }
