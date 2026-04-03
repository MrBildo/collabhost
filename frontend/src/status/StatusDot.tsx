import type { AppStatus, HealthStatus } from '@/api/types'
import { cn } from '@/lib/cn'

type StatusDotProps = {
  status: AppStatus | HealthStatus
  size?: 'sm' | 'md'
}

function StatusDot({ status, size = 'sm' }: StatusDotProps) {
  return (
    <span
      data-testid="status-dot"
      className={cn('wm-status-dot', `wm-status-dot--${status}`, size === 'md' && 'wm-status-dot--md')}
      style={size === 'md' ? { width: 10, height: 10 } : undefined}
      aria-label={status}
    />
  )
}

export { StatusDot }
export type { StatusDotProps }
