import { ActionButton } from '@/actions/ActionButton'
import type { User } from '@/api/types'
import { useAuth } from '@/hooks/use-auth'
import { useCurrentUser } from '@/hooks/use-current-user'
import { useDeactivateUser, useUsers } from '@/hooks/use-users'
import { formatDate } from '@/lib/format'
import { ROUTES } from '@/lib/routes'
import { ConfirmDialog } from '@/shared/ConfirmDialog'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { PermissionGate } from '@/shared/PermissionGate'
import { Spinner } from '@/shared/Spinner'
import { RoleBadge } from '@/status/RoleBadge'
import { StatusDot } from '@/status/StatusDot'
import { DataTable } from '@/tables/DataTable'
import type { Column } from '@/tables/DataTable'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'

type DeactivateTarget = {
  user: User
  isSelf: boolean
  isLastAdmin: boolean
}

function UsersPageContent() {
  const navigate = useNavigate()
  const usersQuery = useUsers()
  const deactivateMutation = useDeactivateUser()
  const { data: currentUser } = useCurrentUser()

  const { logout } = useAuth()
  const [deactivateTarget, setDeactivateTarget] = useState<DeactivateTarget | null>(null)

  const users = usersQuery.data ?? []

  const activeAdminCount = users.filter((u) => u.isActive && u.role === 'administrator').length

  function handleDeactivateClick(user: User): void {
    const isSelf = currentUser?.id === user.id
    const isLastAdmin = user.role === 'administrator' && activeAdminCount <= 1
    setDeactivateTarget({ user, isSelf, isLastAdmin })
  }

  function handleDeactivateConfirm(): void {
    if (!deactivateTarget) return
    const { user, isSelf } = deactivateTarget
    deactivateMutation.mutate(user.id, {
      onSuccess: () => {
        setDeactivateTarget(null)
        if (isSelf) {
          logout()
        }
      },
    })
  }

  const columns: Column<User>[] = [
    {
      key: 'name',
      header: 'Name',
      render: (user) => (
        <span style={{ fontFamily: 'var(--wm-mono)', color: 'var(--wm-text-bright)' }}>{user.name}</span>
      ),
      sortFn: (a, b) => a.name.localeCompare(b.name),
    },
    {
      key: 'role',
      header: 'Role',
      render: (user) => <RoleBadge role={user.role} size="md" />,
      width: '160px',
    },
    {
      key: 'status',
      header: 'Status',
      render: (user) => (
        <div className="flex items-center gap-2">
          <StatusDot status={user.isActive ? 'running' : 'stopped'} />
          <span style={{ color: user.isActive ? 'var(--wm-text)' : 'var(--wm-text-dim)' }}>
            {user.isActive ? 'Active' : 'Inactive'}
          </span>
        </div>
      ),
      width: '120px',
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (user) => (
        <span style={{ color: 'var(--wm-text-dim)', fontFamily: 'var(--wm-mono)', fontSize: 'var(--wm-font-xs)' }}>
          {formatDate(user.createdAt)}
        </span>
      ),
      width: '140px',
    },
    {
      key: 'actions',
      header: '',
      render: (user) => {
        if (!user.isActive) return null

        const isSelf = currentUser?.id === user.id
        const isLastAdmin = user.role === 'administrator' && activeAdminCount <= 1

        return (
          // biome-ignore lint/a11y/useKeyWithClickEvents: stopPropagation wrapper prevents row click from triggering
          <span onClick={(e) => e.stopPropagation()}>
            <ActionButton
              size="sm"
              variant={isSelf ? 'warn' : 'danger'}
              disabled={isLastAdmin}
              onClick={() => handleDeactivateClick(user)}
            >
              {isSelf ? 'Deactivate (you)' : 'Deactivate'}
            </ActionButton>
          </span>
        )
      },
      width: '160px',
      align: 'right',
    },
  ]

  if (usersQuery.isLoading) {
    return (
      <div className="py-8">
        <Spinner />
      </div>
    )
  }

  const confirmTitle = deactivateTarget?.isSelf ? 'Deactivate Your Account' : 'Deactivate User'
  const confirmMessage = deactivateTarget?.isSelf
    ? 'You are about to deactivate your own account. You will be logged out immediately and will not be able to log back in with this key. Only the config admin key or another administrator can restore access.'
    : `Deactivate "${deactivateTarget?.user.name}"? This user will no longer be able to authenticate. Their existing sessions will fail on next request.`

  return (
    <div>
      <div
        className="flex items-baseline justify-between mb-5 pb-3"
        style={{ borderBottom: '1px solid var(--wm-border)' }}
      >
        <h1 className="wm-section-title" style={{ borderBottom: 'none', paddingBottom: 0 }}>
          <span style={{ color: 'var(--wm-text-dim)' }}>{'// '}</span>Users
        </h1>
        <ActionButton variant="amber" size="sm" onClick={() => navigate(ROUTES.userCreate)}>
          + New User
        </ActionButton>
      </div>

      {usersQuery.error && (
        <ErrorBanner
          message={usersQuery.error instanceof Error ? usersQuery.error.message : 'Failed to load users'}
          className="mb-4"
        />
      )}

      {users.length === 0 ? (
        <EmptyState
          title="No users found"
          description="Create your first user to get started."
          action={
            <ActionButton variant="amber" onClick={() => navigate(ROUTES.userCreate)}>
              + New User
            </ActionButton>
          }
        />
      ) : (
        <DataTable
          columns={columns}
          data={users}
          keyFn={(user) => user.id}
          rowClassName={(user) => (!user.isActive ? 'wm-table-row--inactive' : undefined)}
        />
      )}

      <ConfirmDialog
        title={confirmTitle}
        message={confirmMessage}
        confirmLabel="Deactivate"
        confirmVariant="danger"
        isOpen={deactivateTarget !== null}
        isPending={deactivateMutation.isPending}
        onConfirm={handleDeactivateConfirm}
        onCancel={() => setDeactivateTarget(null)}
      />
    </div>
  )
}

function UsersPage() {
  return (
    <PermissionGate requiredRole="administrator">
      <UsersPageContent />
    </PermissionGate>
  )
}

export { UsersPage }
