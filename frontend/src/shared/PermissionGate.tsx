import { ActionButton } from '@/actions/ActionButton'
import { ApiError } from '@/api/client'
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

function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.statusCode === 403
}

function PermissionGate({ requiredRole, children }: PermissionGateProps) {
  const navigate = useNavigate()
  const { data: currentUser, isLoading, isError, error, refetch } = useCurrentUser()

  if (isLoading) {
    return (
      <div className="py-8">
        <Spinner />
      </div>
    )
  }

  // Distinguish a transient identity failure from a genuine permission denial
  // (FE-AUTH-02). A failed `/auth/me` (network blip, 5xx) means we could not
  // verify the operator's role — that is a retryable error, not "Access Denied"
  // (which would wrongly imply they lack permission). Only a settled forbidden
  // (403), or a successful load with the wrong role, is an actual denial.
  if (isError && !isForbidden(error)) {
    return (
      <EmptyState
        title="Couldn't verify permissions"
        description={
          error instanceof Error
            ? `Failed to load your identity: ${error.message}`
            : 'Failed to load your identity. Check your connection and try again.'
        }
        action={
          <ActionButton variant="default" onClick={() => refetch()}>
            Retry
          </ActionButton>
        }
      />
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
