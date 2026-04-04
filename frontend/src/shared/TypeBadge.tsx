import { cn } from '@/lib/cn'

type TypeBadgeProps = {
  label: string
  className?: string
}

function TypeBadge({ label, className }: TypeBadgeProps) {
  return <span className={cn('wm-type-badge', className)}>{label}</span>
}

export { TypeBadge }
export type { TypeBadgeProps }
