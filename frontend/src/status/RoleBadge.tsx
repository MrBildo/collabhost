import type { UserRole } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatRole } from '@/lib/format'

type RoleBadgeProps = {
  role: UserRole
  size?: 'sm' | 'md'
}

function RoleBadge({ role, size = 'sm' }: RoleBadgeProps) {
  return (
    <span
      className={cn('wm-role-badge', `wm-role-badge--${role}`, size === 'md' && 'wm-role-badge--md')}
      aria-label={`Role: ${formatRole(role)}`}
    >
      {formatRole(role)}
    </span>
  )
}

export { RoleBadge }
export type { RoleBadgeProps }
