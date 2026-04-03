import type { AppStatus } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatStatus } from '@/lib/format'

type StatusTextProps = {
  status: AppStatus
  className?: string
}

function StatusText({ status, className }: StatusTextProps) {
  return (
    <span className={cn(`wm-status-text--${status}`, 'text-xs font-medium', className)}>{formatStatus(status)}</span>
  )
}

export { StatusText }
export type { StatusTextProps }
