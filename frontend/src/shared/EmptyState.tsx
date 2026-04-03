import { cn } from '@/lib/cn'
import type { ReactNode } from 'react'

type EmptyStateProps = {
  title: string
  description?: string
  icon?: ReactNode
  action?: ReactNode
  className?: string
}

function EmptyState({ title, description, icon, action, className }: EmptyStateProps) {
  return (
    <div className={cn('wm-empty-state', className)}>
      {icon && <div className="wm-empty-state__icon">{icon}</div>}
      <div className="wm-empty-state__title">{title}</div>
      {description && <p className="wm-empty-state__description">{description}</p>}
      {action && <div className="mt-3">{action}</div>}
    </div>
  )
}

export { EmptyState }
export type { EmptyStateProps }
